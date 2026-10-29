// 收集所有本地化键（C# Loc.T/Loc.F 与 XAML {loc:Tr '...'}）
import fs from 'node:fs';
import path from 'node:path';

const keys = new Set();

function scan(file) {
  const s = fs.readFileSync(file, 'utf8');
  // C#: Loc.T("...") / Loc.F("...", ...) —— 读取普通字符串字面量
  const csRe = /Loc\.(?:T|F)\(/g;
  for (const m of s.matchAll(csRe)) {
    let i = m.index + m[0].length;
    // 跳过空白，期望 "
    while (i < s.length && /\s/.test(s[i])) i++;
    if (s[i] !== '"') continue;
    let j = i + 1;
    let body = '';
    while (j < s.length) {
      const c = s[j];
      if (c === '\\') { body += s[j] + s[j + 1]; j += 2; continue; }
      if (c === '"') break;
      body += c;
      j++;
    }
    try {
      keys.add(JSON.parse('"' + body + '"'));
    } catch {
      keys.add(body);
    }
  }
  // XAML: {loc:Tr '...'}
  for (const m of s.matchAll(/\{loc:Tr '([^']*)'\}/g)) keys.add(m[1]);
}

(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (['obj', 'bin'].includes(e.name)) continue;
      walk(p);
    } else if (/\.(cs|xaml)$/.test(e.name)) scan(p);
  }
})('C:/my_codes/fast-redis/src');

const arr = [...keys].sort((a, b) => a.localeCompare(b, 'zh'));
fs.writeFileSync('C:/my_codes/fast-redis/tools/i18n/keys.json', JSON.stringify(arr, null, 2), 'utf8');
console.log('keys:', arr.length);
