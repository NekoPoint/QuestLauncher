
const fs = require('fs');
const path = require('path');
const sharp = require(process.env.LUCIDE_SHARP_MODULE || 'sharp');
(async () => {
  const dir = path.join(__dirname, 'assets/lucide');
  for (const file of fs.readdirSync(dir).filter(f => f.endsWith('.svg'))) {
    const svg = fs.readFileSync(path.join(dir, file), 'utf8').replaceAll('currentColor', '#ffffff');
    await sharp(Buffer.from(svg)).resize(96, 96).png().toFile(path.join(dir, file.replace('.svg', '.png')));
  }
})();
