<!--
Thanks for contributing. CONTRIBUTING.md has the setup and style notes; this is just the checklist.
Keep the description focused on what changed and why — especially for anything performance- or
format-sensitive.
-->

## What changed

<!-- What does this do, and why? -->

## Why

<!-- The motivation. For a bug fix, what was broken and how does this fix it? -->

## Checklist

- [ ] `dotnet build SIQS.slnx` succeeds (warnings are errors)
- [ ] `dotnet test --solution SIQS.slnx` passes
- [ ] Tests added or updated for behavior changes

## Algorithmic / performance changes

<!-- Delete this section if it doesn't apply. -->

- [ ] The change is **measured**, not assumed faster — numbers and machine below
- [ ] Mathematical invariants hold (relations still satisfy their congruence)
- [ ] Serialized run/relation file formats are unchanged, or the break is called out explicitly

<!-- Measurements: target size, before/after, repetitions, CPU. -->
