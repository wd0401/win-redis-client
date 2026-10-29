// 将 C# 中的中文字符串字面量替换为 Loc.T(...) / Loc.F(...)（以中文原文为翻译键）
// 用法：node rewrite-cs.mjs [--apply]
import fs from 'node:fs';
import path from 'node:path';

const root = 'C:/my_codes/fast-redis/src';
const apply = process.argv.includes('--apply');
const HAN = /[\u4e00-\u9fff]/;

// 数据值：不翻译（{ 文件相对路径: [原文...] }）
const SKIP = {
  'WinRedisClient.Presentation/ViewModels/SettingsViewModel.cs': ['中文'],
};

const files = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (['obj', 'bin'].includes(e.name)) continue;
      walk(p);
    } else if (e.name.endsWith('.cs')) files.push(p);
  }
})(root);

const report = { files: 0, plain: 0, interpolated: 0, skipped: 0, problems: [] };

// 从 src[i] === '{' 开始，返回匹配的 '}' 之后的位置（支持嵌套花括号、字符串、字符字面量）
function skipHole(src, i, verbatim) {
  let depth = 0;
  let j = i;
  while (j < src.length) {
    const c = src[j];
    if (c === '"') {
      // 字符串（可能是插值）
      let k = j + 1;
      while (k < src.length) {
        const d = src[k];
        if (d === '\\') { k += 2; continue; }
        if (d === '"') { if (src[k + 1] === '"' && verbatim) { k += 2; continue; } break; }
        k++;
      }
      j = k + 1;
      continue;
    }
    if (c === "'") {
      let k = j + 1;
      while (k < src.length && src[k] !== "'") { if (src[k] === '\\') k++; k++; }
      j = k + 1;
      continue;
    }
    if (c === '{') depth++;
    else if (c === '}') {
      depth--;
      if (depth === 0) return j + 1;
    }
    j++;
  }
  return src.length;
}

function findTopLevel(s, target, lastOnly = false) {
  let depth = 0, inStr = false, found = -1;
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (inStr) { if (c === '\\') i++; else if (c === '"') inStr = false; continue; }
    if (c === '"') inStr = true;
    else if (c === '(' || c === '[' || c === '{') depth++;
    else if (c === ')' || c === ']' || c === '}') depth--;
    else if (c === target && depth === 0) { found = i; if (!lastOnly) return i; }
  }
  return found;
}

function convertInterpolated(body, verbatim, rel, line) {
  let text = '';
  const args = [];
  let i = 0;
  while (i < body.length) {
    const c = body[i];
    if (c === '{') {
      if (body[i + 1] === '{') { text += '{{'; i += 2; continue; }
      let depth = 1, j = i + 1, inStr = false;
      for (; j < body.length; j++) {
        const d = body[j];
        if (inStr) {
          if (!verbatim && d === '\\') { j++; continue; }
          if (d === '"') inStr = false;
          continue;
        }
        if (d === '"') { inStr = true; continue; }
        if (d === '{') depth++;
        else if (d === '}') { depth--; if (depth === 0) break; }
      }
      if (depth !== 0) { report.problems.push(`${rel}:${line} 未闭合插值`); return null; }
      const inner = body.slice(i + 1, j);
      let expr = inner, align = '', fmt = '';
      const lastColon = findTopLevel(inner, ':', true);
      if (lastColon >= 0) {
        const candidate = inner.slice(lastColon + 1);
        if (/^[A-Za-z0-9#.,%+\-]+$/.test(candidate)) {
          fmt = candidate;
          expr = inner.slice(0, lastColon);
        }
      }
      const commaIdx = findTopLevel(expr, ',');
      if (commaIdx >= 0) {
        align = expr.slice(commaIdx + 1).trim();
        expr = expr.slice(0, commaIdx);
      }
      args.push(expr.trim());
      text += '{' + (args.length - 1) + (align ? ',' + align : '') + (fmt ? ':' + fmt : '') + '}';
      i = j + 1;
      continue;
    }
    if (c === '}' && body[i + 1] === '}') { text += '}}'; i += 2; continue; }
    if (c === '"' && verbatim && body[i + 1] === '"') { text += '"'; i += 2; continue; }
    text += c;
    i++;
  }
  const literal = '"' + text.replace(/\\/g, '\\\\').replace(/"/g, '\\"') + '"';
  return args.length ? `Loc.F(${literal}, ${args.join(', ')})` : `Loc.T(${literal})`;
}

for (const f of files) {
  const rel = path.relative(root, f).replace(/\\/g, '/');
  if (rel.includes('Localization/Localizer.cs')) continue;

  const src = fs.readFileSync(f, 'utf8');
  if (!HAN.test(src)) continue;

  let out = '';
  let i = 0;
  let changed = false;
  let line = 1;

  while (i < src.length) {
    const c = src[i];

    // 行注释
    if (c === '/' && src[i + 1] === '/') {
      const end = src.indexOf('\n', i);
      const stop = end === -1 ? src.length : end;
      out += src.slice(i, stop); i = stop; continue;
    }
    // 块注释
    if (c === '/' && src[i + 1] === '*') {
      const end = src.indexOf('*/', i + 2);
      const stop = end === -1 ? src.length : end + 2;
      const chunk = src.slice(i, stop);
      out += chunk;
      for (const ch of chunk) if (ch === '\n') line++;
      i = stop; continue;
    }
    // 字符字面量
    if (c === "'") {
      let j = i + 1;
      while (j < src.length && src[j] !== "'") { if (src[j] === '\\') j++; j++; }
      out += src.slice(i, j + 1);
      i = j + 1;
      continue;
    }
    // 字符串（可能带 $ / @ 前缀）
    let pre = '';
    let q = i;
    while ((src[q] === '$' || src[q] === '@') && q - i < 2) { pre += src[q]; q++; }
    if (src[q] === '"') {
      const verbatim = pre.includes('@');
      const interpolated = pre.includes('$');
      let j = q + 1;
      while (j < src.length) {
        const d = src[j];
        if (verbatim) {
          if (d === '"' && src[j + 1] === '"') { j += 2; continue; }
          if (d === '"') break;
          if (interpolated && d === '{') {
            if (src[j + 1] === '{') { j += 2; continue; }
            j = skipHole(src, j, true);
            continue;
          }
          j++;
        } else {
          if (d === '\\') { j += 2; continue; }
          if (d === '"') break;
          if (interpolated && d === '{') {
            if (src[j + 1] === '{') { j += 2; continue; }
            j = skipHole(src, j, false);
            continue;
          }
          j++;
        }
      }
      const raw = src.slice(i, j + 1);
      const body = src.slice(q + 1, j);
      const startLine = line;
      for (const ch of raw) if (ch === '\n') line++;

      if (!HAN.test(body)) { out += raw; i = j + 1; continue; }

      const skipList = SKIP[rel] || [];
      if (!interpolated && skipList.includes(body)) {
        out += raw; i = j + 1; report.skipped++; continue;
      }

      let replacement;
      if (interpolated) {
        replacement = convertInterpolated(body, verbatim, rel, startLine);
        if (replacement === null) { out += raw; i = j + 1; continue; }
        report.interpolated++;
      } else {
        replacement = `Loc.T(${raw})`;
        report.plain++;
      }

      out += replacement;
      changed = true;
      i = j + 1;
      continue;
    }

    // 普通字符（若前缀不是字符串，则原样输出前缀字符）
    if (q > i) {
      out += src.slice(i, q);
      for (const ch of src.slice(i, q)) if (ch === '\n') line++;
      i = q;
      continue;
    }
    out += c;
    if (c === '\n') line++;
    i++;
  }

  if (changed) {
    report.files++;
    if (!out.includes('using WinRedisClient.Core.Localization;')) {
      const usingMatches = [...out.matchAll(/^using [^;]+;\s*$/gm)];
      if (usingMatches.length) {
        const last = usingMatches[usingMatches.length - 1];
        const insertAt = last.index + last[0].length;
        out = out.slice(0, insertAt) + '\nusing WinRedisClient.Core.Localization;' + out.slice(insertAt);
      } else {
        out = 'using WinRedisClient.Core.Localization;\n\n' + out;
      }
    }
    if (apply) fs.writeFileSync(f, out, 'utf8');
  }
}

console.log(JSON.stringify(report, null, 2));
