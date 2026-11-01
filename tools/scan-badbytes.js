'use strict'
// ============================================================
//  scan-badbytes.js — 全库无效 UTF-8 字节扫描(R169 #6 / R170 批次固化)
//
//  用法:  node tools/scan-badbytes.js [repoRoot]
//
//  扫描 git ls-files 的全部跟踪文件(勿按目录/文件名清单人肉圈范围——
//  R169 的清零结论就因圈掉了 McpServer 测试侧而漏网 1 处),逐文件
//  校验 UTF-8 往返一致性,并报告 U+FFFD 替换字符。
//
//  退出码: 0 = 干净;1 = 发现问题(可直接挂 CI)。
//  已知误报: 二进制资产(ico/png/...)按扩展名跳过。
// ============================================================
const fs = require('fs')
const path = require('path')
const { execSync } = require('child_process')

const root = path.resolve(process.argv[2] || '.')
const BIN_EXT = new Set(['.ico', '.png', '.jpg', '.jpeg', '.gif', '.woff', '.woff2', '.ttf', '.snk', '.dll', '.exe', '.pdb', '.zip'])

const files = execSync('git ls-files', { cwd: root, encoding: 'utf8' }).split('\n').map(s => s.trim()).filter(Boolean)
let bad = 0
let scanned = 0
for (const f of files) {
  if (BIN_EXT.has(path.extname(f).toLowerCase())) continue
  const p = path.join(root, f)
  let buf
  try { buf = fs.readFileSync(p) } catch { continue }
  scanned++
  const text = buf.toString('utf8')
  if (!Buffer.from(text, 'utf8').equals(buf)) { console.log('INVALID UTF-8:', f); bad++ }
  else if (text.includes('\uFFFD')) { console.log('U+FFFD present:', f); bad++ }
}
console.log(`scanned ${scanned} files, ${bad} with invalid UTF-8 / U+FFFD`)
process.exit(bad === 0 ? 0 : 1)
