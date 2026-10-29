// 提取 XAML 中所有用户可见的中文字符串（属性值 + 元素文本内容）
import fs from 'node:fs';
import path from 'node:path';

const root = 'C:/my_codes/fast-redis/src';
const files = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (e.name === 'obj' || e.name === 'bin' || e.name === 'Themes') continue;
      walk(p);
    } else if (e.name.endsWith('.xaml')) files.push(p);
  }
})(root);

const HAN = /[\u4e00-\u9fff]/;
const ATTR = /(\b(?:Text|Content|Header|ToolTip|Title|Watermark|Tag|Description|AutomationProperties\.Name)\s*=\s*)"([^"]*)"/g;

const out = [];
const seen = new Map();

for (const f of files) {
  const src = fs.readFileSync(f, 'utf8');
  const rel = path.relative(root, f).replace(/\\/g, '/');

  const add = (zh, kind, line) => {
    if (!HAN.test(zh)) return;
    if (zh.includes('{') || zh.includes('}')) return; // markup / StringFormat 由人工处理
    const key = zh;
    if (!seen.has(key)) {
      seen.set(key, { zh, kind, count: 0, files: [] });
      out.push(seen.get(key));
    }
    const rec = seen.get(key);
    rec.count++;
    if (rec.files.length < 3) rec.files.push(`${rel}:${line}`);
  };

  const lineOf = (idx) => src.slice(0, idx).split('\n').length;

  // 属性
  for (const m of src.matchAll(ATTR)) {
    add(m[2], 'attr', lineOf(m.index));
  }
  // 元素文本内容（排除注释）
  const noComments = src.replace(/<!--[\s\S]*?-->/g, (c) => c.replace(/[^\n]/g, ' '));
  for (const m of noComments.matchAll(/>([^<]+)</g)) {
    const text = m[1].trim();
    if (text) add(text, 'inner', lineOf(m.index));
  }
}

fs.writeFileSync('C:/my_codes/fast-redis/tools/i18n/strings.raw.json', JSON.stringify(out, null, 2), 'utf8');
console.log(`files=${files.length} distinct=${out.length}`);
