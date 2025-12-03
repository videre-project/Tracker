// This script rewrites the generated api.d.ts file to strip namespaces from exported type names and their references.
// Usage: node scripts/stripNamespaceFromTypes.js <input> <output>

const fs = require('fs');

if (process.argv.length < 4) {
  console.error('Usage: node scripts/stripNamespaceFromTypes.js <input> <output>');
  process.exit(1);
}

const inputFile = process.argv[2];
const outputFile = process.argv[3];

const content = fs.readFileSync(inputFile, 'utf8');

// Map to store schema path -> simple alias
const aliasMap = {};
const generatedExports = [];

// Regex to find schema definitions in components.schemas
// Matches: "Namespace.Type": {
// We rely on the fact that namespaced types contain dots.
const schemaDefRegex = /"([\w.]+)"\s*:\s*{/g;

let match;
while ((match = schemaDefRegex.exec(content)) !== null) {
  const fullSchemaName = match[1];

  // We only care about namespaced types (containing dots)
  if (fullSchemaName.includes('.')) {
    const simpleName = fullSchemaName.split('.').pop();

    // Add to alias map for replacing references
    // Handle both single and double quotes styles that might appear in references
    aliasMap[`components['schemas']['${fullSchemaName}']`] = simpleName;
    aliasMap[`components["schemas"]["${fullSchemaName}"]`] = simpleName;

    generatedExports.push(`export type ${simpleName} = components['schemas']['${fullSchemaName}'];`);
  }
}

// Replace references in the content
let lines = content.split(/\r?\n/);

for (let i = 0; i < lines.length; i++) {
  for (const [schemaPath, alias] of Object.entries(aliasMap)) {
    // Replace all occurrences of the schemaPath with the alias
    const escapedPath = schemaPath.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    lines[i] = lines[i].replace(new RegExp(escapedPath, 'g'), alias);
  }
}

// Append the generated exports
const finalContent = lines.join('\n') + '\n\n' + generatedExports.join('\n');

fs.writeFileSync(outputFile, finalContent, 'utf8');
console.log(`Stripped namespaces from exported types and replaced references in ${outputFile}`);
