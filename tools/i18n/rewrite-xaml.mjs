// 将 XAML 属性中的中文字符串替换为 {loc:Tr '...'}，并补充 xmlns:loc
// 用法：node rewrite-xaml.mjs [--apply]
import fs from 'node:fs';
import path from 'node:path';

const root = 'C:/my_codes/fast-redis/src';
const apply = process.argv.includes('--apply');
const HAN = /[\u4e00-\u9fff]/;

const files = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (['obj', 'bin', 'Themes'].includes(e.name)) continue;
      walk(p);
    } else if (e.name.endsWith('.xaml')) files.push(p);
  }
})(root);

const ATTR = /(\b(?:Text|Content|Header|ToolTip|Title|Watermark|Tag|Description|AutomationProperties\.Name)\s*=\s*)"([^"]*)"/g;
const report = { files: 0, replaced: 0, skipped: [] };

for (const f of files) {
  const rel = path.relative(root, f).replace(/\\/g, '/');
  let src = fs.readFileSync(f, 'utf8');
  let changed = false;

  src = src.replace(ATTR, (whole, pre, value) => {
    if (!HAN.test(value)) return whole;
    if (value.includes('{') || value.includes('}')) {
      report.skipped.push(`${rel}: markup 值 ${JSON.stringify(value)}`);
      return whole;
    }
    if (value.includes("'")) {
      report.skipped.push(`${rel}: 含单引号 ${JSON.stringify(value)}`);
      return whole;
    }
    if (value.includes('&')) {
      report.skipped.push(`${rel}: 含实体 ${JSON.stringify(value)}`);
      return whole;
    }
    changed = true;
    report.replaced++;
    return `${pre}"{loc:Tr '${value}'}"`;
  });

  if (changed) {
    if (!src.includes('xmlns:loc=')) {
      src = src.replace(
        /(xmlns:x="http:\/\/schemas\.microsoft\.com\/winfx\/2006\/xaml")/,
        '$1\n             xmlns:loc="clr-namespace:WinRedisClient.App.Localization"');
    }
    report.files++;
    if (apply) fs.writeFileSync(f, src, 'utf8');
  }
}

console.log(JSON.stringify({ files: report.files, replaced: report.replaced, skipped: report.skipped.length }, null, 2));
if (report.skipped.length) console.log(report.skipped.slice(0, 20).join('\n'));
