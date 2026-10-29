import fs from 'node:fs';

const merged = {};
for (const n of [1, 2, 3, 4, 5]) {
  Object.assign(merged, JSON.parse(fs.readFileSync(`C:/my_codes/fast-redis/tools/i18n/en.part${n}.json`, 'utf8')));
}
merged['该连接当前处于已连接状态，删除后将断开连接。'] =
  'This connection is currently connected and will be disconnected when deleted.';

const keys = JSON.parse(fs.readFileSync('C:/my_codes/fast-redis/tools/i18n/keys.json', 'utf8')).filter(Boolean);

const norm = (s) => s.replace(/\\n/g, '\n');
const has = (k) => Object.prototype.hasOwnProperty.call(merged, k) || Object.prototype.hasOwnProperty.call(merged, norm(k));

const missing = keys.filter((k) => k !== '中文' && !has(k));
console.log('missing:', missing.length);
for (const m of missing) console.log(JSON.stringify(m));

fs.writeFileSync('C:/my_codes/fast-redis/src/WinRedisClient.Core/Localization/en.json',
  JSON.stringify(merged, null, 2), 'utf8');
console.log('written', Object.keys(merged).length);
