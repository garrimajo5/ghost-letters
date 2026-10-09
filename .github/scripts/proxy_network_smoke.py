"""Exercise the production proxy network, including recovery from its old IP collision.

Uses disposable sleep containers, no published ports, credentials or application data.
"""
import copy
import json
import os
from pathlib import Path
import subprocess
import tempfile
import uuid


def run(args, *, check=True, env=None):
    result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace",
                            timeout=120, env=env)
    if check and result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    return result


root = Path(__file__).resolve().parents[2]
environment = dict(os.environ, POSTGRES_PASSWORD="test-only", JWT_SIGNING_KEY="test-only-key-not-for-production-0000",
                   DOMAIN="example.invalid")
production = json.loads(run(["docker", "compose", "-f", str(root / "deploy/docker-compose.prod.yml"),
                             "config", "--format", "json"], env=environment).stdout)
api_ip = production["services"]["api"]["networks"]["edge"]["ipv4_address"]
proxy_ip = production["services"]["caddy"]["networks"]["edge"]["ipv4_address"]
assert api_ip != proxy_ip
assert production["services"]["api"]["environment"]["Proxy__KnownProxies"] == proxy_ip
assert not production["services"]["api"].get("ports"), "API must remain private"

fixed = {"services": {}, "networks": copy.deepcopy(production["networks"])}
# Compose config inserts project-specific names; let the isolated project name its own networks.
for network in fixed["networks"].values():
    network.pop("name", None)
for name in ("api", "caddy"):
    fixed["services"][name] = {
        "image": "postgres:16-alpine", "entrypoint": ["sleep"], "command": ["120"],
        "networks": copy.deepcopy(production["services"][name]["networks"]),
    }
fixed["services"]["caddy"]["depends_on"] = ["api"]
broken = copy.deepcopy(fixed)
broken["services"]["api"]["networks"]["edge"].pop("ipv4_address")
project = "ghost-proxy-test-" + uuid.uuid4().hex[:10]

with tempfile.TemporaryDirectory(prefix="ghost-proxy-test-") as directory:
    path = Path(directory) / "compose.json"
    command = ["docker", "compose", "-p", project, "-f", str(path)]

    def address(service):
        container = run(command + ["ps", "-q", service]).stdout.strip()
        data = json.loads(run(["docker", "inspect", container]).stdout)[0]
        assert data["State"]["Running"], service
        return data["NetworkSettings"]["Networks"][project + "_edge"]["IPAddress"]

    try:
        path.write_text(json.dumps(broken), encoding="utf-8")
        failed = run(command + ["up", "-d"], check=False)
        assert failed.returncode != 0 and "address already in use" in failed.stderr.lower(), failed.stdout + failed.stderr
        assert address("api") == proxy_ip, "reproduces API taking the trusted proxy address"
        print("Reproduced: dynamic API allocation occupies Caddy's address.")
        path.write_text(json.dumps(fixed), encoding="utf-8")
        run(command + ["up", "-d"])
        assert address("api") == api_ip
        assert address("caddy") == proxy_ip
        run(command + ["up", "-d"])
        assert address("api") == api_ip and address("caddy") == proxy_ip
        print("Passed: existing broken stack recovers and repeated deployment keeps distinct addresses.")
    finally:
        run(command + ["down", "--volumes", "--remove-orphans"])
