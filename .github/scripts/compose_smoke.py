"""Смоук-проверка локального стенда (docker compose): сервер отвечает, карты из манифеста,
двое игроков проходят путь от входа до старта партии. Только стандартная библиотека Python."""
import json
import sys
import time
import urllib.error
import urllib.request
import uuid

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:8080"
API = BASE + "/api/v1"


def call(method, path, body=None, token=None, expect=200):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(API + path if path.startswith("/") else path, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, timeout=15) as r:
            status, text = r.status, r.read().decode()
    except urllib.error.HTTPError as e:
        status, text = e.code, e.read().decode()
    if status != expect:
        raise SystemExit(f"::error title=compose smoke::{method} {path} → {status}, ждали {expect}: {text[:500]}")
    return json.loads(text) if text else None


def wait_health():
    for _ in range(90):
        try:
            with urllib.request.urlopen(BASE + "/health", timeout=3) as r:
                if r.read().decode() == "Healthy":
                    return
        except Exception:
            pass
        time.sleep(2)
    raise SystemExit("::error title=compose smoke::сервер не поднялся за 3 минуты")


def login(nick):
    return call("POST", "/auth/guest", {"deviceId": f"smoke-{uuid.uuid4()}", "nickname": nick})


def main():
    wait_health()
    manifest = json.load(open("client/assets/cards/cards.json", encoding="utf-8"))
    known = {c["id"] for s in manifest["sets"] for c in s["cards"]}

    host, guest = login("Хост"), login("Гость")
    ht, gt = host["accessToken"], guest["accessToken"]
    lobby = call("POST", "/lobbies", {"title": "Смоук", "settings": {"rounds": 1}}, ht)
    call("POST", f"/lobbies/{lobby['code']}/join", {"mode": "player"}, gt)
    call("POST", f"/lobbies/{lobby['id']}/ready", {"ready": True}, gt)
    game_id = call("POST", f"/lobbies/{lobby['id']}/start", {}, ht)["gameId"]

    snap = call("GET", f"/games/{game_id}/view", token=ht)
    board = [c for row in snap["view"]["board"] for c in row["cards"]]
    unknown = [c for c in board if c not in known]
    if unknown:
        raise SystemExit(f"::error title=compose smoke::на поле карты не из манифеста: {unknown[:5]}")
    if sorted(r["nickname"] for r in snap["roster"]) != ["Гость", "Хост"]:
        raise SystemExit(f"::error title=compose smoke::странный состав: {snap['roster']}")

    for token in (ht, gt):
        call("POST", f"/games/{game_id}/commands", {"type": "AckRole", "payload": {}}, token)
    phase = call("GET", f"/games/{game_id}/view", token=gt)["view"]["phase"]
    if phase != "Night":
        raise SystemExit(f"::error title=compose smoke::после знакомства с ролями фаза {phase}, ждали Night")

    print(f"OK: поле из {len(board)} карт манифеста, партия {game_id} в фазе {phase}")


if __name__ == "__main__":
    main()
