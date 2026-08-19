# Circular project-reference fixture

Proves a two-node project-reference cycle: `A ↔ B`. The fixture is intentionally not buildable as a normal dependency graph; it exists so graph extraction can detect cycles deterministically.
