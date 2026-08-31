# PR Review Checklist

For collaborators who review PRs without owning the affected area. Your job
is to catch what the author's tunnel vision hides: correctness, breakage,
scope creep, and things that don't survive contact with a real build.

Not all PRs are handled the same way:

- **Small, low-risk changes** (think: a few files, a few dozen lines, within
  an area like translations or docs) may be set up to auto-merge after a
  couple of approvals — check the PR page for merge requirements. On PRs like
  that, **your approval may be what merges the code**, so only give it when
  you've actually done the checks below.
- **Everything else** goes through the maintainers, who make the final call;
  your review is input to theirs.

## 1. Scope and intent

- [ ] The PR is in scope for the project — it fits what OpenUtau is and how
      it's headed. If you're not sure, leave it alone: don't review or
      approve, let the maintainers take it.
- [ ] The PR does what the description claims and nothing else: every changed
      file is explainable by the PR's purpose (unrelated files → request
      changes, ask for a split), and the linked issue, if any, is genuinely
      resolved by this diff and referenced so it closes on merge.
- [ ] For bug fixes: the fix addresses the cause, not just the symptom — check
      the logic actually covers the reported repro.

## 2. Read the diff

- [ ] Follow the changed logic through its callers (does the change alter their
      behavior?) and probe edge cases: null/empty inputs, missing files or
      voice models, zero-length ranges, CJK or very long file names; failures
      should be caught and surfaced in user-understandable terms, not
      swallowed or dumped as stack traces.
- [ ] Consistency with the surrounding code: naming, patterns, and behavior
      match how the same area is already written; UI changes use the localized
      string tables (no hard-coded strings), and layout behaves at different
      window sizes / DPI if layout is touched.
- [ ] Nothing accidental in the diff: no debug leftovers (`Debug.WriteLine`,
      test `MessageBox.Show`/`Console.WriteLine`, `TODO`/`FIXME`,
      commented-out code), no personal data (absolute paths, machine names,
      local test assets), no binaries or large assets committed.

## 3. Build and run

- [ ] Build the PR branch (or use the CI artifact) — compiles. Reproduce the
      original bug on the pre-PR build when feasible, then verify the fix on
      the PR build.
- [ ] Exercise the feature beyond the happy path: undo/redo, canceling dialogs,
      repeated toggling, corner cases.

## 4. Regression smoke test

The minimum bar before approving a non-trivial PR — and for PRs touching a
subsystem, also smoke-test adjacent features, not just the changed one:

- [ ] Open an existing project, load a singer / voice model
- [ ] Move some notes / pitch points, play back

## 5. When to defer to the maintainers

If you are uncertain in any way, do not approve — post your notes as a
comment and let the maintainers decide. Typical cases:

- [ ] New dependencies, or build/CI changes. (Upgraded dependencies are fine
      to approve yourself, as long as you tested the build.)
- [ ] Large changes touching many files, including "chore" PRs touching
      hundreds of files
- [ ] A new major feature, or behavior beyond the PR's description
- [ ] Platform-sensitive changes that you could only test on one platform —
      these need testing on all platforms before approval. Say which platform
      you tested on, so others know what's still missing
- [ ] Anything you couldn't fully follow

## 6. Verdict

- **Approve** — only when you are confident: you read the diff, built it,
  tested the change and the smoke test, and can explain the change if asked.
- **Leave it to the maintainers** — if any of section 5 applies, or anything
  else leaves you uncertain; comment instead of approving.
- **Request changes** — scope creep, correctness problem, or a regression you
  reproduced. Each point must be specific (file/line or repro steps), not a
  general concern.
- **Make it clear what you actually did** — if you built and tested the PR,
  say so and note how (OS, source build or CI artifact, what you tried); if
  you only read the diff, say that too. An untested review should never look
  like a tested one.
