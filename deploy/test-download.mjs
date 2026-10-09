import {readFileSync} from 'node:fs';
import {runInNewContext} from 'node:vm';
import test from 'node:test';
import assert from 'node:assert/strict';

const html = readFileSync(new URL('./www/download/index.html', import.meta.url), 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];
async function page({exists = true, ios = false} = {}) {
  const nodes = new Map();
  const requests = [];
  const get = id => {
    if (!nodes.has(id)) nodes.set(id, {classList: {add() {}}, parentNode: {insertBefore() {}},
      addEventListener(event, callback) {this[event] = callback;}});
    return nodes.get(id);
  };
  runInNewContext(script, {
    document: {getElementById: get}, navigator: {userAgent: ios ? 'iPhone' : 'Android', platform: '', maxTouchPoints: 0},
    fetch: async (url, options) => {
      requests.push({url, options});
      return {ok: url.endsWith('.apk') ? exists : false, json: async () => null};
    },
  });
  await new Promise(resolve => setImmediate(resolve));
  let prevented = false;
  get('apk').click({preventDefault() {prevented = true;}});
  return {prevented, note: get('apk-note').textContent, requests};
}
test('APK link is absolute and available file uses native browser download', async () => {
  assert.match(html, /href="\/download\/ghost-letters.apk" download/);
  const result = await page();
  assert.equal(result.prevented, false);
  assert.ok(result.requests.some(r => r.url === '/download/ghost-letters.apk' && r.options.method === 'HEAD'));
});
test('missing APK is explained instead of claiming download succeeded', async () => {
  const result = await page({exists: false});
  assert.equal(result.prevented, true);
  assert.match(result.note, /ещё готовится/);
});
test('iPhone receives web installation guidance', async () => {
  const result = await page({ios: true});
  assert.equal(result.prevented, true);
  assert.match(result.note, /Для iPhone/);
});
