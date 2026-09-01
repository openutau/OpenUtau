// Auto-merge decision for small PRs. See .github/workflows/auto-merge.yml.
// Runs with the workflow GITHUB_TOKEN (needs contents:write and
// pull-requests:write); the approvers-team lookup needs TEAM_TOKEN, which
// GITHUB_TOKEN cannot stand in for. Prints the full decision trail and
// appends it to the job summary.
//
// This file and RULES_FILE are checked out from the base branch, the workflow
// triggers on pull_request_target so it is read from the base branch too, and
// any PR touching .github/ is refused below: a PR cannot influence its own
// decision. Keep policy in RULES_FILE rather than in the environment, so that
// stays true even if the trigger is ever changed back to pull_request.
import fs from 'node:fs';

const TOKEN = process.env.GITHUB_TOKEN;
const TEAM_TOKEN = process.env.TEAM_TOKEN;
const REPO = process.env.GITHUB_REPOSITORY;
const PR = Number(process.env.PR_NUMBER);
const RULES_FILE = '.github/auto-merge-rules.json';
const CFG = JSON.parse(fs.readFileSync(RULES_FILE, 'utf8'));
const org = REPO.split('/')[0];
const EVENT = { name: process.env.EVENT_NAME, action: process.env.EVENT_ACTION };
const MARKER = '<!-- auto-merge-rules -->';

const report = (line) => {
  console.log('[auto-merge] ' + line);
  if (process.env.GITHUB_STEP_SUMMARY) fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, line + '\n');
};
const notEligible = (reason) => {
  report(`NOT ELIGIBLE: ${reason}`);
  process.exit(0);
};

async function getJSON(path) {
  const res = await fetch('https://api.github.com' + path, {
    headers: {
      Authorization: `Bearer ${TOKEN}`,
      Accept: 'application/vnd.github+json',
      'User-Agent': 'openutau-auto-merge',
    },
  });
  if (!res.ok) {
    const err = new Error(`API ${res.status} ${path}: ${(await res.text()).slice(0, 300)}`);
    err.status = res.status;
    throw err;
  }
  return res.json();
}

// Paginated GET (100/page). Returns null when the result set exceeds 300
// items, since the API caps list endpoints there.
async function getAll(path) {
  let items = [];
  for (let page = 1; page <= 3; page++) {
    const batch = await getJSON(`${path}?per_page=100&page=${page}`);
    items = items.concat(batch);
    if (batch.length < 100) return items;
  }
  return null;
}

const pr = await getJSON(`/repos/${REPO}/pulls/${PR}`);
if (pr.state !== 'open') notEligible(`PR is ${pr.state}`);
if (pr.draft) notEligible('PR is a draft');
// head.repo is null when the source fork was deleted or is invisible to the
// token; that is not this repo, so it must be rejected too.
if (!CFG.allow_forks && pr.head.repo?.full_name !== REPO)
  notEligible('PR does not come from a branch of this repository');

const [commits, reviews, files] = await Promise.all([
  getAll(`/repos/${REPO}/pulls/${PR}/commits`),
  getAll(`/repos/${REPO}/pulls/${PR}/reviews`),
  getAll(`/repos/${REPO}/pulls/${PR}/files`),
]);
if (commits === null) notEligible('PR has more than 300 commits');
if (files === null) notEligible('PR changes more than 300 files');
if (reviews === null) notEligible('PR has more than 300 reviews');

// Who wrote the PR: the head author plus every commit author. Their
// approvals never count.
const authors = new Set([pr.user.login]);
for (const c of commits) {
  if (c.author && c.author.login) authors.add(c.author.login);
}

// Reviews only count if they were made against the current head commit (a
// push voids earlier reviews, mirroring the UI). Commit dates are NOT usable
// for this: committer dates are client-supplied and can be backdated, and the
// commits endpoint truncates, so the last listed commit need not be the head.
const onHead = (r) => r.commit_id === pr.head.sha;

if (reviews.some((r) => r.state === 'CHANGES_REQUESTED' && onHead(r)))
  notEligible('changes requested on the current head commit');

const approvals = [
  ...new Map(
    reviews
      .filter(
        (r) =>
          r.state === 'APPROVED' &&
          onHead(r) &&
          r.user &&
          !r.user.login.endsWith('[bot]') &&
          !authors.has(r.user.login)
      )
      .map((r) => [r.user.login, r])
  ).values(),
];

// A file belongs to a rule if it equals a path, or is below a directory
// path. Only paths with a trailing "/" act as directory prefixes; anything
// else must be the exact file name.
const inRule = (filename, rule) =>
  rule.paths.some((p) => (p.endsWith('/') ? filename.startsWith(p) : filename === p));

// GitHub reports additions/deletions as 0 for binary blobs, pure renames and
// mode-only changes, so line counts cannot bound them. Such files are never
// auto-mergeable, whichever rule they fall under.
const opaque = (f) =>
  f.status === 'renamed' || (f.additions || 0) + (f.deletions || 0) === 0;

const perRule = CFG.rules.map((rule) => {
  const rs = files.filter((f) => inRule(f.filename, rule));
  return {
    rule,
    files: rs.length,
    lines: rs.reduce((s, f) => s + (f.additions || 0) + (f.deletions || 0), 0),
    opaque: rs.filter(opaque).map((f) => f.filename),
  };
});

// Which touched rules meet their own file/line limits, counted over the
// files in their paths only?
const touched = perRule.filter((p) => p.files > 0);
const withinLimits = (p) =>
  (p.rule.max_files === undefined || p.files < p.rule.max_files) && p.lines < p.rule.max_lines;

// Team pages are org-members-only, so spell the team out as well as link it.
const TEAM_LINK = `[@${org}/${CFG.approvers_team}](https://github.com/orgs/${org}/teams/${CFG.approvers_team})`;
const CHECKLIST_LINK =
  `[PR review checklist](https://github.com/${REPO}/blob/${pr.base.ref}/.github/PR_REVIEW_CHECKLIST.md)`;

// One-shot comment: when the PR becomes ready, explain which rules apply and
// what they require, based on the diff at that moment. Posted at most once
// (marker comment), no matter how the diff changes afterwards.
const becameReady =
  EVENT.name === 'pull_request_target' &&
  ((EVENT.action === 'opened' && !pr.draft) || EVENT.action === 'ready_for_review');
if (becameReady) {
  const alreadyCommented = await (async () => {
    for (let page = 1; page <= 20; page++) {
      const comments = await getJSON(`/repos/${REPO}/issues/${PR}/comments?per_page=100&page=${page}`);
      if (comments.some((c) => (c.body || '').includes(MARKER))) return true;
      if (comments.length < 100) return false;
    }
    return true; // give up rather than risk a duplicate
  })();
  if (!alreadyCommented) {
    const lines = [MARKER, '**Auto-merge rules** (checked when this PR became ready; posted once, not updated):', ''];
    const covered = files.every((f) => touched.some((p) => inRule(f.filename, p.rule)));
    if (touched.length === 0 || !covered) {
      lines.push('- This PR changes files outside the auto-merge rule paths — a maintainer will merge it.');
    } else {
      for (const p of touched) {
        const filePart =
          p.rule.max_files === undefined
            ? `${p.files} files (no limit)`
            : `${p.files} files (max ${p.rule.max_files - 1})`;
        const linePart = `${p.lines} lines (max ${p.rule.max_lines - 1})`;
        lines.push(
          `- ${p.rule.name} (${p.rule.paths.join(', ')}): ${filePart}, ${linePart} — ` +
            (withinLimits(p) ? 'within limits' : '**exceeds a limit**')
        );
      }
      const allOk = touched.every(withinLimits);
      if (allOk) {
        const required = Math.max(...touched.map((p) => p.rule.required_approvals));
        lines.push(
          `- Auto-merges once **${required} approval${required > 1 ? 's' : ''}** ${required > 1 ? 'are' : 'is'} in place, ` +
            `from members of ${TEAM_LINK}. Approvals from anyone who authored a commit here never count, ` +
            'and only reviews of the latest commit count — a new push voids earlier approvals.'
        );
      } else {
        lines.push('- Size limits not met — a maintainer will merge this PR.');
      }
    }
    lines.push(
      '',
      `Reviewing this PR? Please work through the ${CHECKLIST_LINK} — it covers what to check ` +
        'before approving, and when to leave the call to the maintainers.'
    );
    const posted = await fetch(`https://api.github.com/repos/${REPO}/issues/${PR}/comments`, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${TOKEN}`,
        Accept: 'application/vnd.github+json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ body: lines.join('\n') }),
    });
    // Non-fatal: the comment is informational, the decision below stands
    // either way. But say so, rather than claiming a post that never landed.
    if (posted.ok) report('posted auto-merge rules comment');
    else report(`failed to post rules comment (${posted.status}): ${(await posted.text()).slice(0, 300)}`);
  } else {
    report('rules comment already exists, skipping');
  }
}

// Belt and braces: this workflow, its script and its rules must never be
// auto-merged, whatever the rules happen to say.
const ci = files.filter((f) => f.filename.startsWith('.github/'));
if (ci.length > 0)
  notEligible(`PR modifies CI configuration: ${ci.map((f) => f.filename).join(', ')}`);

// Every changed file must be covered by at least one rule.
if (!files.every((f) => perRule.some((p) => inRule(f.filename, p.rule))))
  notEligible('changes outside all rule paths');

for (const p of touched) {
  if (p.opaque.length > 0)
    notEligible(`rule "${p.rule.name}": binary or renamed files cannot be size-checked: ${p.opaque.join(', ')}`);
  if (p.rule.max_files !== undefined && p.files >= p.rule.max_files)
    notEligible(`rule "${p.rule.name}": touches ${p.files} files (must be < ${p.rule.max_files})`);
  if (p.lines >= p.rule.max_lines)
    notEligible(`rule "${p.rule.name}": ${p.lines} changed lines (must be < ${p.rule.max_lines})`);
}
if (touched.length === 0) notEligible('no changed files');
// The strictest approval requirement among the touched rules applies.
const required = Math.max(...touched.map((p) => p.rule.required_approvals));
report(
  'size ok: ' +
    touched.map((p) => `"${p.rule.name}" ${p.files} files / ${p.lines} lines`).join(', ') +
    `; required approvals: ${required}`
);

// Count approvals that come from members of the approvers team. GITHUB_TOKEN
// has no org scope and always fails here, so this needs TEAM_TOKEN.
if (!TEAM_TOKEN)
  notEligible('AUTO_MERGE_TEAM_TOKEN is not configured, cannot verify approvers-team membership');
const teamApprovals = [];
for (const r of approvals) {
  const res = await fetch(`https://api.github.com/orgs/${org}/teams/${CFG.approvers_team}/memberships/${r.user.login}`, {
    headers: { Authorization: `Bearer ${TEAM_TOKEN}`, Accept: 'application/vnd.github+json' },
  });
  // A 200 also covers invitations that have not been accepted yet; only
  // "active" membership makes someone an approver.
  if (res.status === 200) {
    const m = await res.json();
    if (m.state === 'active') teamApprovals.push(r.user.login);
    else report(`ignoring ${r.user.login}: team membership is "${m.state}"`);
  } else if (res.status !== 404) {
    notEligible(`team membership check failed: ${res.status} for ${r.user.login}`);
  }
}
report(
  `approvals on ${pr.head.sha.slice(0, 7)}: ${approvals.map((r) => r.user.login).join(', ') || 'none'}; ` +
    `from @${org}/${CFG.approvers_team}: ${teamApprovals.join(', ') || 'none'}`
);

if (teamApprovals.length < required)
  notEligible(`needs ${required} approval(s) from @${org}/${CFG.approvers_team}, has ${teamApprovals.length}`);

// CI gate: every pr-test check run on the head commit must be completed and
// passing. Only the gating workflow's runs are considered — in particular
// NOT this auto-merge workflow's own check run, which is in_progress while
// we are deciding and would deadlock the merge. Runs are re-created on each
// push, so this always reflects the latest head.
const GATING_WORKFLOW = CFG.gating_workflow || 'pr-test';
async function getCheckRuns(sha) {
  let runs = [];
  for (let page = 1; page <= 5; page++) {
    const data = await getJSON(`/repos/${REPO}/commits/${sha}/check-runs?per_page=100&page=${page}`);
    runs = runs.concat(data.check_runs);
    if (data.check_runs.length < 100 || runs.length >= data.total_count) break;
  }
  // Check-run names are "<job id> (<matrix summary>)".
  return runs.filter((r) => r.name.startsWith(`${GATING_WORKFLOW} `) || r.name === GATING_WORKFLOW);
}
const runs = await getCheckRuns(pr.head.sha);
if (runs.length === 0) notEligible('no CI check runs on the head commit yet — waiting for CI');
const pending = runs.filter((r) => r.status !== 'completed');
if (pending.length > 0)
  notEligible(`CI still running: ${pending.map((r) => r.name).join(', ')}`);
const failing = runs.filter((r) => !['success', 'neutral', 'skipped'].includes(r.conclusion));
if (failing.length > 0)
  notEligible(`CI failing: ${failing.map((r) => `${r.name} (${r.conclusion})`).join(', ')}`);
report(`CI ok: ${runs.length} check(s) passed on ${pr.head.sha.slice(0, 7)}`);

report(`ELIGIBLE -> merging with "${CFG.merge_method}"`);
const res = await fetch(`https://api.github.com/repos/${REPO}/pulls/${PR}/merge`, {
  method: 'PUT',
  headers: {
    Authorization: `Bearer ${TOKEN}`,
    Accept: 'application/vnd.github+json',
    'Content-Type': 'application/json',
  },
  // sha pins the merge to the commit whose approvals and CI we just checked;
  // GitHub refuses the merge if the head moved while we were deciding.
  body: JSON.stringify({ merge_method: CFG.merge_method, sha: pr.head.sha }),
});
if (res.ok) {
  report('merged');
} else {
  // e.g. merge conflict, or branch-protection checks outside our control
  report(`merge failed (${res.status}): ${(await res.text()).slice(0, 300)} — left for manual merge`);
}
