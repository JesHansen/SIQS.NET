# Resume artifact validation

Resume does not trust a file merely because its text parses. Every completed phase is checked from
the start of the pipeline, and the first phase with an invalid output is recomputed together with all
downstream phases.

The deep checks below run when planning a resume. A phase that has just produced an artifact retains
the lightweight presence, parse, dimension, and metadata validation used by normal runs; it does not
immediately reread and re-prove its own trusted output. This keeps successful factorization wall time
unchanged while applying the stronger trust boundary exactly where persisted files re-enter execution.

The structural tier is always on. It validates factor-base indexing and roots; relation identity,
kind, exponent/parity and sign invariants; matrix row ordering and relation correspondence;
dependency row identity and zero matrix products; and status-specific factor-result fields and
products. These checks read each relevant artifact once and are linear in its compact representation.

Mathematical checks are always complete for the factor base, dependencies, and reported factor
pairs. Raw and filtered relation congruences can dominate resume time on multi-million-relation runs,
so they use a deterministic sample of up to 32 records spread evenly from the first through the
last record of each artifact. Files with at most 32 records are checked completely. Distributed
relations additionally receive complete mathematical verification before ingest, independently of
this resume sampling policy.

Validation errors name the artifact and invariant but do not include complete relation payloads.
This keeps diagnostics useful without copying very large or potentially hostile records into
`job.json` and `events.log`.
