// This script rewrites the generated api.d.ts file to strip namespaces from exported type names and their references.
// Usage: node scripts/stripNamespaceFromTypes.js <input> <output>

const fs = require('fs');
const path = require('path');

if (process.argv.length < 4) {
  console.error('Usage: node scripts/stripNamespaceFromTypes.js <input> <output>');
  process.exit(1);
}

const inputFile = process.argv[2];
const outputFile = process.argv[3];

const content = fs.readFileSync(inputFile, 'utf8');

// Helper to extract the last part after the last dot in a quoted string
function getSimpleTypeName(schemaRef) {
  // e.g. components["schemas"]["MTGOSDK.API.Collection.Card"] -> "Card"
  const match = schemaRef.match(/\["([\w.]+)"\]$/);
  if (!match) return schemaRef;
  const full = match[1];
  const parts = full.split('.');
  return parts[parts.length - 1];
}

// Find all type alias definitions and build a map from schema path to alias
const aliasMap = {};
let newContent = content.replace(/export type ([A-Za-z0-9_]+) = components\['schemas'\]\['([\w.]+)'\];/g, (m, oldName, schema) => {
  const simple = schema.split('.').pop();
  aliasMap[`components['schemas']['${schema}']`] = simple;
  aliasMap[`components[\"schemas\"][\"${schema}\"]`] = simple;
  return `export type ${simple} = components['schemas']['${schema}'];`;
});

// Replace all other instances of the schema path (single or double quoted) with the alias, except in the type alias definition itself
const lines = newContent.split(/\r?\n/);
const aliasDefRegex = /^export type (\w+) = components\['schemas'\]\['([\w.]+)'\];$/;
for (let i = 0; i < lines.length; i++) {
  const match = lines[i].match(aliasDefRegex);
  if (match) continue; // skip alias definition line
  for (const [schemaPath, alias] of Object.entries(aliasMap)) {
    // Replace all occurrences of the schemaPath with the alias
    lines[i] = lines[i].replace(new RegExp(schemaPath.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'g'), alias);
  }
}
const finalContent = lines.join('\n');

fs.writeFileSync(outputFile, finalContent, 'utf8');
console.log(`Stripped namespaces from exported types and replaced references in ${outputFile}`);
