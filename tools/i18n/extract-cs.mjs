// 提取 C# 中的中文字符串字面量（普通字符串 + 插值字符串），排除注释
import fs from 'node:fs';
import path from 'node:path';

const root = 'C:/my_codes/fast-redis/src';
const files = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (e.name === 'obj' || e.name === 'bin') continue;
      walk(p);
    } else if (e.name.endsWith('.cs')) files.push(p);
  }
})(root);

const HAN = /[\u4e00-\u9fff]/;
const out = [];

for (const f of files) {
  const src = fs.readFileSync(f, 'utf8');
  const rel = path.relative(root, f).replace(/\\/g, '/');
  // 去掉注释，保留行号
  const noComments = src
    .replace(/\/\*[\s\S]*?\*\//g, (c) => c.replace(/[^\n]/g, ' '))
    .replace(/\/\/[^\n]*/g, (c) => c.replace(/[^\n]/g, ' '));

  const lineOf = (idx) => noComments.slice(0, idx).split('\n').length;

  // 普通字符串 "..." 与逐字 @"..." / 插值 $"..." / $@"..." / @$"..."
  const re = /(\$@?|@\$?|@)?"((?:[^"\\\n]|\\.|"")*)"/g;
  for (const m of noComments.matchAll(re)) {
    const prefix = m[1] || '';
    const body = m[2];
    if (!HAN.test(body)) continue;
    out.push({
      file: rel,
      line: lineOf(m.index),
      interpolated: prefix.includes('$'),
      verbatim: prefix.includes('@'),
      text: body,
      raw: m[0],
    });
  }
}

fs.writeFileSync('C:/my_codes/fast-redis/tools/i18n/cs.raw.json', JSON.stringify(out, null, 2), 'utf8');
console.log(`files=${files.length} literals=${out.length} interpolated=${out.filter(x => x.interpolated).length}`);
