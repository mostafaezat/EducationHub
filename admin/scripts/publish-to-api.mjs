import { cpSync, existsSync, mkdirSync, rmSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const adminDir = dirname(dirname(fileURLToPath(import.meta.url)));
const source = join(adminDir, 'dist', 'educationhub-admin', 'browser');
const apiProjectDir = join(dirname(adminDir), 'src', 'EducationHub.API');
const target = join(apiProjectDir, 'wwwroot');

if (!existsSync(join(source, 'index.html'))) {
  console.error(`No built SPA found at ${source}. Run "ng build" first.`);
  process.exit(1);
}

rmSync(target, { recursive: true, force: true });
mkdirSync(target, { recursive: true });
cpSync(source, target, { recursive: true });
console.log(`Published ${source} -> ${target}`);
