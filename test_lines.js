const sectionText = `Backend/Application/Features/{Agencies,Skills}/**
Backend/WebAPI/Controllers/{Agencies,Skills}/**
Backend/Infrastructure/Modules/{Agencies,Skills}/**
Backend/Tests/{Agencies,Skills}/**
Backend/Domain/Entities/*.{Agencies,Skills}.cs
Mobile/lib/features/agencies/**
Mobile/test/features/agencies/**
Frontend/src/features/{agencies,skills}/**
.spec/plan/M5-*.md
.spec/contracts/{agencies,skills}.md
.spec/contracts/openapi.json`;

const lines = (text) =>
  text.split('\n').map((l) => l.replace(/^[-*]\s*/, '').replace(/`/g, '').trim()).filter((l) => l && l !== '_No response_');

console.log('Lines:');
console.log(lines(sectionText));