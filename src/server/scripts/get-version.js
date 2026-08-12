#!/usr/bin/env node
const fs = require('fs');
const path = require('path');

// The package.json is at the solution root, which is 3 levels up from scripts/
const packageJsonPath = path.join(__dirname, '..', '..', '..', 'package.json');
const packageJson = JSON.parse(fs.readFileSync(packageJsonPath, 'utf8'));
console.log(packageJson.version);