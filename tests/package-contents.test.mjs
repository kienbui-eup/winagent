import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

test('published runtime includes every relative JavaScript dependency', () => {
  const root = fileURLToPath(new URL('..', import.meta.url));
  const output = execFileSync(process.execPath, [process.env.npm_execpath, 'pack', '--dry-run', '--ignore-scripts', '--json'], { cwd: root, encoding: 'utf8' });
  const [{ files }] = JSON.parse(output);
  const included = new Set(files.map(file => file.path));
  for (const file of included) {
    if (!file.endsWith('.mjs')) continue;
    const source = readFileSync(path.join(root, file), 'utf8');
    for (const match of source.matchAll(/(?:from\s*|import\s*\()\s*['"](\.[^'"]+)['"]/g)) {
      const dependency = path.posix.normalize(path.posix.join(path.posix.dirname(file), match[1]));
      assert.ok(included.has(dependency), `${file} needs missing package file ${dependency}`);
    }
  }
});
