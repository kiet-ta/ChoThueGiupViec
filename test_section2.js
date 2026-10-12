const fs = require('fs');
const body = fs.readFileSync('issue_237.body', 'utf8');
const section = (body, heading) => {
  const m = body.replace(/\r/g, '').match(new RegExp(`### ${heading}\\n\\n([\\s\\S]*?)(?=\\n### |$)`));
  return m ? m[1].trim() : '';
};
const lines = (text) =>
  text.split('\n').map((l) => l.replace(/^[-*]\s*/, '').replace(/`/g, '').trim()).filter((l) => l && l !== '_No response_');

console.log('Allowed section:');
console.log('[' + section(body, 'Allowed paths') + ']');
console.log('Lines allowed:');
console.log(lines(section(body, 'Allowed paths')));
console.log('Acceptance section:');
console.log('[' + section(body, 'Acceptance criteria') + ']');
console.log('Lines acceptance:');
console.log(lines(section(body, 'Acceptance criteria')));