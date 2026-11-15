// Imprime un HTML a PDF A4 con Chromium (Playwright). Uso: node imprimir_pdf.js entrada.html salida.pdf "Título"
const { chromium } = require('playwright');
(async () => {
  const [input, output, title] = process.argv.slice(2);
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.goto('file://' + input, { waitUntil: 'load' });
  await page.pdf({
    path: output,
    format: 'A4',
    printBackground: true,
    displayHeaderFooter: true,
    headerTemplate: '<div></div>',
    footerTemplate: `<div style="font-size:7pt;color:#666;width:100%;padding:0 16mm;display:flex;justify-content:space-between;">
      <span>${(title || '').replace(/</g, '&lt;')}</span><span>Página <span class="pageNumber"></span> de <span class="totalPages"></span></span></div>`,
    margin: { top: '16mm', bottom: '18mm', left: '16mm', right: '16mm' },
  });
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
