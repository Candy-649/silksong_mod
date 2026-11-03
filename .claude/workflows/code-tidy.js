export const meta = {
  name: 'code-tidy',
  description: 'Retire old ways in the co-op mod: find code that still follows a decision a better one replaced, code written twice, dead code and stale words; bring it onto the decision in force; build, commit, one review',
  whenToUse: 'Once a week after a version is published, or when the user asks to tidy the code (整理代码). Args: {since: "<commit of the last tidy>", date: "YYYY-MM-DD", first: true only on the very first run, maxApply: 10}',
  phases: [
    { title: 'Decisions', detail: 'bring the register of decisions in force up to date' },
    { title: 'Find', detail: 'one reader per group: old ways left, new code written twice, dead code, stale words' },
    { title: 'Plan', detail: 'merge, check, sort, pick what goes in now' },
    { title: 'Apply', detail: 'one item at a time on the real checkout: edit, build, commit, no push' },
    { title: 'Review', detail: 'one review of the tidy commits' },
  ],
}

// ---- inputs ----

const A = args || {}
const SINCE = A.since
const DATE = A.date
const FIRST = A.first === true
const MAX_APPLY = typeof A.maxApply === 'number' ? A.maxApply : 10
const CHUNK = 5
if (!SINCE || !DATE) {
  throw new Error('code-tidy needs args {since: "<commit of the last tidy>", date: "YYYY-MM-DD"}')
}

const REPO = 'D:/programming/silksong_mod/SSMP'
const TOOLS = 'D:/programming/silksong_mod'
const REGISTER = TOOLS + '/tidy/decisions.md'
const LOG = TOOLS + '/tidy/log.md'
const MEMORY = 'C:/Users/48688/.claude/projects/D--programming-silksong-mod/memory'

// The user's rule for subagents: Opus at high effort, never max, and no agents of their own.
const OPUS = { agentType: 'opus-high', effort: 'high' }

const GROUPS = [
  {
    key: 'A',
    name: "the players' characters: their moves, effects, looks, skins, the figure, input, heads on the map",
    folders: [
      'SSMP/Animation/', 'SSMP/Game/Client/Skin/', 'SSMP/Game/Client/PlayerManager.cs',
      'SSMP/Game/Client/ClientPlayerData.cs', 'SSMP/Game/Client/HarpoonWatch.cs', 'SSMP/Game/Client/BindCoop.cs',
      'SSMP/Game/Client/NeedolinCoop.cs', 'SSMP/Game/Client/NeedolinRemoteAudio.cs',
      'SSMP/Game/Client/OwnFigureOutline.cs', 'SSMP/Game/Client/OwnMapIconOutline.cs',
      'SSMP/Game/Client/SpriteOutline.cs', 'SSMP/Game/Client/FightOutline.cs',
      'SSMP/Game/Client/MapHeadPointer.cs', 'SSMP/Game/Client/MapManager.cs',
    ],
  },
  {
    key: 'B',
    name: 'creatures and their copies: their moves, hits, health, targets, dice, thrown things, summons, deaths',
    folders: [
      'SSMP/Game/Client/Entity/', 'SSMP/Fsm/', 'SSMP/Hooks/', 'SSMP/Resource/',
      'SSMP/Game/Client/GamePatcher*.cs', 'SSMP/Game/Client/EnemyHealthCoop.cs',
      'SSMP/Game/Client/PlayerTargetRegistry.cs', 'SSMP/Game/Client/Save/CoopHits*.cs',
      'SSMP/Game/Client/Save/HitCounters.cs', 'SSMP/Game/Client/Save/HitMachines.cs',
      'SSMP/Game/Client/Save/SharedDice.cs', 'SSMP/Game/Client/Save/ThrownThings.cs',
      'SSMP/Game/Client/Save/ToldParts.cs', 'SSMP/Game/Client/Save/CoopSave.ShownParts.cs',
      'SSMP/Game/Client/Save/CoopSave.CreatureDeaths.cs', 'SSMP/Game/Client/Save/CoopAttackCheck.cs',
      'SSMP/Game/Client/Save/CoopStateCheck.cs',
    ],
  },
  {
    key: 'C',
    name: 'rooms, the world and saves: boss rooms and arenas, mechanisms, lifts, story, wishes, rescue, waits',
    folders: [
      'SSMP/Game/Client/Save/ (every file not listed for group B)', 'SSMP/Game/Client/ArenaCoop*.cs',
      'SSMP/Game/Client/BossRoomCoop*.cs', 'SSMP/Game/Client/BenchCoop.cs', 'SSMP/Game/Client/MinigameCoop*.cs',
      'SSMP/Game/Client/MinigameMirror.cs', 'SSMP/Game/Client/PersonalPlaces.cs', 'SSMP/Game/Client/ScenePath.cs',
      'SSMP/Game/Server/',
    ],
  },
  {
    key: 'D',
    name: 'network, UI and the rest: packets and sending, the server, chat, menus, settings, logs, helpers',
    folders: [
      'SSMP/Networking/', 'SSMP/Ui/', 'SSMP/Game/Command/', 'SSMP/Game/Settings/', 'SSMP/Game/*.cs',
      'SSMP/Game/Client/ClientManager.cs', 'SSMP/Game/Client/PauseManager.cs', 'SSMP/Logging/', 'SSMP/Util/',
      'SSMP/Serialization/', 'SSMP/Concurrency/', 'SSMP/Api/ (the public addon API: read it, never change it)',
      'SSMP/SSMPPlugin.cs', 'SSMPServer/', 'ProtocolCheck/', 'MMS/',
    ],
  },
]

const CONTEXT = [
  `Project: SSMP, a BepInEx plugin in C# that makes Hollow Knight: Silksong playable in online co-op. The code is in ${REPO} (git branch sync-fixes; the solution SSMP.sln holds SSMP/, SSMPServer/, ProtocolCheck/ and MMS/). Two players use it, the user and a friend; one of them hosts. Both always run the same build: the mod refuses to connect two different ones.`,
  `Why this tidy. The user, 2026-11-01: "感觉代码需要定期整理一下，不然规则会越来越复杂，越来越冗余" and "因为我们在改代码的时候有时候也会慢慢想出更好的方法，所以一些以前的决策就不再有效了，所以才要整理代码". As better ways are found, earlier decisions stop holding, but the code written under them stays and goes on doing things the old way. The tidy finds that code and brings it onto the decision in force, or takes it out, so that each kind of thing is done one way and each rule lives in one place.`,
  `The register of decisions in force is ${REGISTER}: for each decision, the rule now, the way it replaced, the signs of the old way in code, the known exceptions and its source. Earlier runs are in ${LOG}.`,
].join('\n\n')

const RULES = [
  'Rules for this work:',
  '- Do not launch subagents.',
  `- Never touch anything under D:/STEAM (the game folder) or outside ${REPO} and ${TOOLS}. The memory folder ${MEMORY} is read only.`,
  "- Do not run FsmScan, KnightFrames, ILPeek or anything else that reads the game's own files; read the mod's code and git history only.",
  '- Text that a player reads (Chinese) names no bosses, areas, characters or story; words like 队友, 怪, 房间, 椅子 and 梦境 are fine. Internal notes may name classes and scenes.',
].join('\n')

// ---- output shapes ----

const SITE = {
  type: 'object',
  properties: { file: { type: 'string' }, line: { type: 'integer' }, what: { type: 'string' } },
  required: ['file'],
}

const DECISIONS_SCHEMA = {
  type: 'object',
  properties: {
    total: { type: 'integer' },
    changed: {
      type: 'array',
      items: {
        type: 'object',
        properties: { id: { type: 'string' }, group: { type: 'string' }, name: { type: 'string' } },
        required: ['id', 'group', 'name'],
      },
    },
    notes: { type: 'string' },
  },
  required: ['total', 'changed', 'notes'],
}

const FIND_SCHEMA = {
  type: 'object',
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          kind: { type: 'string', enum: ['old-way', 'written-twice', 'dead', 'stale-words', 'new-exception'] },
          decision: { type: 'string' },
          title: { type: 'string' },
          sites: { type: 'array', items: SITE },
          proposal: { type: 'string' },
          playChanges: { type: 'boolean' },
          playNote: { type: 'string' },
          why: { type: 'string' },
          size: { type: 'string', enum: ['small', 'large'] },
        },
        required: ['kind', 'decision', 'title', 'sites', 'proposal', 'playChanges', 'why', 'size'],
      },
    },
    notes: { type: 'string' },
  },
  required: ['findings', 'notes'],
}

const PLAN_SCHEMA = {
  type: 'object',
  properties: {
    apply: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string' },
          kind: { type: 'string', enum: ['quiet', 'visible'] },
          decision: { type: 'string' },
          title: { type: 'string' },
          steps: { type: 'string' },
          sites: { type: 'array', items: SITE },
          why: { type: 'string' },
          commitSubject: { type: 'string' },
          player: { type: 'string' },
        },
        required: ['id', 'kind', 'decision', 'title', 'steps', 'sites', 'why', 'commitSubject'],
      },
    },
    later: {
      type: 'array',
      items: {
        type: 'object',
        properties: { title: { type: 'string' }, reason: { type: 'string' }, files: { type: 'array', items: { type: 'string' } } },
        required: ['title', 'reason'],
      },
    },
    big: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          title: { type: 'string' }, decision: { type: 'string' }, size: { type: 'string' },
          files: { type: 'array', items: { type: 'string' } },
        },
        required: ['title', 'size'],
      },
    },
    ask: {
      type: 'array',
      items: {
        type: 'object',
        properties: { title: { type: 'string' }, question: { type: 'string' }, recommendation: { type: 'string' } },
        required: ['title', 'question', 'recommendation'],
      },
    },
    exceptions: {
      type: 'array',
      items: {
        type: 'object',
        properties: { decision: { type: 'string' }, place: { type: 'string' }, reason: { type: 'string' } },
        required: ['decision', 'place', 'reason'],
      },
    },
    rejected: {
      type: 'array',
      items: {
        type: 'object',
        properties: { title: { type: 'string' }, reason: { type: 'string' } },
        required: ['title', 'reason'],
      },
    },
  },
  required: ['apply', 'later', 'big', 'ask', 'exceptions', 'rejected'],
}

const APPLY_SCHEMA = {
  type: 'object',
  properties: {
    results: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string' },
          outcome: { type: 'string', enum: ['committed', 'skipped', 'stopped'] },
          commit: { type: 'string' },
          reason: { type: 'string' },
          files: { type: 'array', items: { type: 'string' } },
        },
        required: ['id', 'outcome'],
      },
    },
    head: { type: 'string' },
  },
  required: ['results', 'head'],
}

const REVIEW_SCHEMA = {
  type: 'object',
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          commit: { type: 'string' },
          file: { type: 'string' },
          line: { type: 'integer' },
          severity: { type: 'string', enum: ['high', 'medium', 'low'] },
          summary: { type: 'string' },
          scenario: { type: 'string' },
        },
        required: ['commit', 'file', 'severity', 'summary', 'scenario'],
      },
    },
    verdict: { type: 'string' },
  },
  required: ['findings', 'verdict'],
}

// ---- prompts ----

function decisionsPrompt() {
  const how = FIRST
    ? [
      'The register does not exist yet: build it from two sources.',
      `1. The memory of earlier sessions, ${MEMORY}. MEMORY.md is its index, one line per file. Files of type feedback hold the user's rules for how the mod is designed and how the work is done; project files hold designs and the history of what was tried. Read the index first, then open the files whose line points at a rule or a design choice, above all where a later choice replaced an earlier one (look for USER quotes, "replaced", "instead of", "no longer", "REMOVED", "withdrawn", "v2", "rewritten"). Skim long history files for those; skip files that only record facts about the game.`,
      `2. The commits: git -C ${REPO} log --format="%h %ad%n%s%n%b" --date=short. Commit bodies quote the user where they set a rule (USER <date> "...") and say what an approach replaced ("rather than", "instead of", "no longer", "takes the place of").`,
    ].join('\n')
    : [
      `Read the register, then what happened since the last tidy: the commits (git -C ${REPO} log --format="%h %ad%n%s%n%b" --date=short ${SINCE}..HEAD) and the memory files in ${MEMORY} whose frontmatter 'modified' date is later than the newest run in ${LOG} (MEMORY.md is the index).`,
      'Add every decision made in them. Where a new decision replaces one in the register, mark the old entry "Replaced by D<nn>" and carry its old way into the new entry\'s Replaced line. Update entries whose rule changed.',
    ].join('\n')
  return [
    CONTEXT,
    RULES,
    `Task: ${FIRST ? 'build' : 'bring up to date'} the register of decisions in force, ${REGISTER}.`,
    how,
    'What goes in: a choice of HOW the mod does a kind of thing, which code can follow or not. For example "a move sends only its steps (the character\'s clips), and every game plays the move\'s effects from them" (it replaced one message per effect; USER 10-31 "其他的动画都绑定在这两个消息上"), or "the host rolls a shared roll and the copies use its numbers" (it replaced each game rolling its own). Also a game rule that replaced an earlier one, when the earlier one left code behind (for example a look that is no longer synced). Not: facts about the game, research notes, the cause of one bug, release and packing steps, how to talk to the user.',
    'Format (Markdown, English; the user\'s words stay in Chinese, in quotes):',
    '# Decisions in force',
    'One paragraph: what the file is (the decisions the code should follow, and what each replaced), and that the code tidy workflow (.claude/workflows/code-tidy.js) and the main session keep it.',
    "## A. The players' characters",
    '## B. Creatures and their copies',
    '## C. Rooms, the world and saves',
    '## D. Network, UI and the rest',
    'Each entry:',
    '### D<nn> <short name>',
    '- Now: the rule, in one to three sentences.',
    '- Replaced: the earlier way or ways, with when and why; "nothing" if it was the first rule.',
    '- Old-way signs: what code that still follows the old way looks like: class and member names, message or packet types, code patterns, things to grep for.',
    '- Exceptions: places that keep the old way on purpose, each with its reason; "none known" if there are none.',
    "- Source: memory file names and commit shas, with the user's words where there are some.",
    'Number the entries D01, D02, ... through the whole file in group order. A number is never reused: a replaced entry keeps its number and says "Replaced by D<nn>".',
    'Every decision needs a source; do not invent any. Merge entries that state the same rule. Keep the file under about 700 lines.',
    `Write the file to ${REGISTER} (make the folder if needed). Change no other file.`,
    'Return the number of entries in force, the entries that are new or changed in this run (all of them on a first build) with their group letters, and notes on anything you were unsure of.',
  ].join('\n')
}

function findPrompt(g, decisions) {
  const mine = decisions.changed.filter(d => d.group === g.key).map(d => d.id)
  const focus = FIRST || !mine.length
    ? `every decision of group ${g.key}`
    : `the decisions of group ${g.key} that are new or changed since the last tidy (${mine.join(', ')}) first, then the rest of group ${g.key}`
  return [
    CONTEXT,
    RULES,
    `You read for group ${g.key}: ${g.name}.`,
    `Read ${REGISTER} in full (code can break a decision of any group), and ${LOG} if it exists: the 'Left for later' lists of earlier runs are candidates to look at again.`,
    'Look for these:',
    `1. Old ways left in the code. For ${focus}: search the whole code (SSMP/, SSMPServer/, ProtocolCheck/) for code that still does what the decision replaced. Use its old-way signs, and think of the other forms the old way can take. Skip what the entry lists as an exception. For each place, say whether it is a leftover (it should follow the decision in force, or go) or a new exception (it keeps the old way for a reason the register should record).`,
    `2. New code written the old way or written twice. The code changed since the last tidy (${SINCE}) in these files and folders: ${g.folders.join(', ')}. See git -C ${REPO} log --stat ${SINCE}..HEAD and git -C ${REPO} diff ${SINCE}..HEAD -- <paths>. Check it against every decision in the register, not only those of group ${g.key}, and look for a rule, a table or a helper that already existed somewhere else and was written again (search the whole code for the older one).`,
    '3. Plain leftovers in those files: code that nothing reaches any more, and comments, docs or log text that say what the code no longer does. Before calling something unreached, search for its name in strings, nameof, reflection, Harmony patches named by strings, PlayMaker action and FSM names, and IL hooks. SSMP/Api is a public API for addons: leave it. Enum values and packet IDs that fix the numbers of later ones stay.',
    'For each finding give:',
    '- kind: old-way, written-twice, dead, stale-words or new-exception;',
    '- decision: the register ID it is about, or "" for a plain leftover;',
    '- title: one line;',
    '- sites: every place, as file and line;',
    '- proposal: what to change, exactly enough for another engineer to do it;',
    '- playChanges: false if both games behave just as now afterwards; true if what a player sees or does changes;',
    '- playNote: if playChanges, what changes (for example "the effect comes from the clip instead of a message: the same look, it can start a frame apart");',
    '- why: the evidence: what reaches the code, what the copies do differently, how the decision in force covers this case;',
    '- size: small (one commit, under about 150 lines touched) or large (several commits, or a new mechanism).',
    'Prefer real leftovers to cosmetics: no renames, formatting, reordering, or rewrites of code that is written once and follows the decisions. At most 20 findings: if there are more, keep those that take out the most old-way code, and say in notes what you left out. In notes, also say briefly what you checked and found clean.',
    'Read only: no edits, no builds, nothing that changes either repository.',
  ].join('\n')
}

function planPrompt(reports) {
  return [
    CONTEXT,
    RULES,
    `Below are the findings of ${reports.length} readers, as JSON. Make the plan of this tidy (${DATE}, commits ${SINCE}..HEAD).`,
    '1. Merge findings that are the same leftover seen from two sides. Drop cosmetic ones.',
    '2. Check in the code yourself the claims that decide an outcome: that nothing reaches what is called dead (search names and strings), that the decision in force covers the case (read its source in the register), and whether play changes.',
    '3. Sort every finding into one of:',
    '   quiet: change it with no change in play (both games; host and joiner; a player who comes into the room later; a reconnect);',
    "   visible: bring it onto the decision in force where that changes what players see, in the way the decision already settled. Cite the decision and the user's words: the user's own rule is what lets this go in without asking;",
    '   exception: keep it; the register should record why;',
    '   ask: the decisions in force do not settle it, or two of them pull different ways: the user decides;',
    '   big: right, but too large for one run (several commits, or a new mechanism): it gets its own work later; estimate its size;',
    '   rejected: wrong, or not worth it, with the reason.',
    `4. Pick what goes in now: quiet and visible items of size small, at most ${MAX_APPLY}, the independent and safest first, quiet before visible. Good items that do not fit go to 'later' with the reason. One item is one commit. Give each picked item an id (T1, T2, ...), steps written exactly (files, members, what moves where), and a commit subject in the style of the repository (git -C ${REPO} log -20 --format=%s: one plain English sentence that says what changes and why).`,
    "5. For each visible item write 'player': in plain Chinese, for a player who is not a programmer, what they will notice, in at most two sentences, with no code words.",
    "6. For each ask item write 'question': in plain Chinese, the choice with two or three options; and give your recommendation.",
    `7. Write the exceptions into ${REGISTER}, under their decision's Exceptions line. Add a section for this run at the top of ${LOG} (make the file if needed, titled "# Code tidy log", sections newest first): "## ${DATE}, ${SINCE}..<HEAD's short sha>", with the counts, the picked items (titles), 'Left for later' (the later and big items, one line each with their files), 'For the user' (the ask items' titles) and the number rejected. Change no other file, and nothing in ${REPO}.`,
    'Findings:',
    JSON.stringify(reports),
  ].join('\n')
}

function applyPrompt(chunk, offset) {
  return [
    CONTEXT,
    RULES,
    `Apply these ${chunk.length} items of the tidy plan to ${REPO}, in order, one commit each.${offset ? ' Earlier items of the plan were applied before you, by another engineer.' : ''}`,
    `Before you start: git -C ${REPO} status --short must print nothing; if it prints anything, change nothing and report every item as stopped.`,
    'For each item:',
    '1. Read its sites again. If it no longer holds, or you are not sure play stays as the plan says, skip it and say why. Do not widen an item past its steps: what you find on the way goes into your report, not into the code.',
    `2. Edit. Keep each file's line endings: some .cs files are CRLF and some LF (check with: file <path>). Use small Python scripts that read and write bytes (py, open(path, 'rb') and open(path, 'wb')), or the Edit tool followed by that check; never Git Bash sed -i, which strips the CRs of CRLF files. Afterwards git -C ${REPO} diff --stat must show only the lines you meant, never whole files.`,
    `3. Build: cd ${REPO} && dotnet build SSMP/SSMP.csproj -c Release -p:LangVersion=preview. It must print 已成功生成 and 0 个错误, and no warning but the two known CS0618 in HarpoonWatch.cs (2 个警告). If the item touches code that SSMPServer or ProtocolCheck compile too (Networking, Game/Server, shared helpers), build those as well: dotnet build SSMPServer/SSMPServer.csproj -c Release -p:LangVersion=preview, and the same for ProtocolCheck/ProtocolCheck.csproj. If a build fails and a small fix inside the item does not mend it, undo the item and skip it.`,
    `4. Commit: write the message to a file outside the repository, then git -C ${REPO} commit -q -a -F <file> < /dev/null (git add new files first, git rm removed ones). The message: the subject from the plan (adjusted if the change came out different); a blank line; a body in plain English that says what was done the old way, which decision it follows now (register ID and its source) or why it was dead, every place changed, and why play stays the same (quiet) or what players will see (visible); then the lines "Code tidy ${DATE}." and "Version <the Version in SSMP/SSMP.csproj without its last .0>. Not tried in the game"; a blank line; and "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>". Do not push.`,
    'Never: push; change packet layouts or IDs, save formats, or the public API under SSMP/Api; take out a log line (the user reads the logs to find bugs, one line per event); edit installer/INSTALL.txt (the main session writes the notes); touch files outside the repository.',
    'Return one result per item (its id; committed with the commit sha, or skipped or stopped with the reason) and HEAD at the end.',
    'Items:',
    JSON.stringify(chunk),
  ].join('\n')
}

function reviewPrompt(committed, picked) {
  const byId = {}
  for (const p of picked) byId[p.id] = p
  const list = committed.map(c => {
    const p = byId[c.id] || {}
    return { commit: c.commit, id: c.id, kind: p.kind || '', decision: p.decision || '', title: p.title || '', why: p.why || '' }
  })
  return [
    CONTEXT,
    RULES,
    `Review the commits of this tidy in ${REPO}: ${committed.map(c => c.commit).join(', ')}. Each is listed below with its plan: quiet means it claims no change in play; visible means it brings code onto a decision of the register.`,
    'For quiet commits, find any change in what either game does: host or joiner; the player whose game runs the room or the other one; a player who comes into the room later; a reconnect; the order or timing of calls; what a shared static now holds for two former users; exceptions and nulls; what is logged, sent or saved; and anything that still reaches what was removed (reflection, Harmony patches by name, PlayMaker names, IL hooks).',
    'For visible commits, check that they follow the decision exactly, keep both games in step (any difference between the two games is a bug), and leave no part of the old way half in place.',
    "For all of them: new garbage on per-frame or per-message paths (LINQ, closures, boxing; the players' laptops are weak), new warnings, lost log lines.",
    'One pass. For each finding: the commit, file and line, severity (high: wrong play, or the two games out of step; medium: likely wrong in some case; low: small), what is wrong, and a concrete case where it shows. If you find nothing, say so. Read only: change nothing.',
    'Commits:',
    JSON.stringify(list),
  ].join('\n')
}

// ---- the run ----

phase('Decisions')
const decisions = await agent(decisionsPrompt(), {
  ...OPUS, label: FIRST ? 'build the register' : 'update the register', phase: 'Decisions', schema: DECISIONS_SCHEMA,
})
if (!decisions) throw new Error('The register step did not finish; nothing else ran and the code is unchanged.')
log(`Register: ${decisions.total} decisions in force, ${decisions.changed.length} new or changed in this run`)

phase('Find')
// A barrier on purpose: the plan merges what two readers found from two sides.
const found = await parallel(GROUPS.map(g => () => agent(findPrompt(g, decisions), {
  ...OPUS, label: `find ${g.key}`, phase: 'Find', schema: FIND_SCHEMA,
})))
const reports = GROUPS.map((g, i) => ({
  group: g.key,
  findings: found[i] ? found[i].findings : null,
  notes: found[i] ? found[i].notes : 'this reader did not finish',
}))
const missing = reports.filter(r => !r.findings).map(r => r.group)
if (missing.length) log(`Group(s) ${missing.join(', ')} not covered this run: the reader did not finish.`)
const count = reports.reduce((n, r) => n + (r.findings ? r.findings.length : 0), 0)
log(`${count} findings from ${GROUPS.length - missing.length} readers`)
if (!count) return { since: SINCE, date: DATE, decisions, reports, plan: null, results: [], review: null }

phase('Plan')
const plan = await agent(planPrompt(reports), { ...OPUS, label: 'plan', phase: 'Plan', schema: PLAN_SCHEMA })
if (!plan) throw new Error('The plan step did not finish; the code is unchanged.')
const picked = plan.apply.slice(0, MAX_APPLY)
if (plan.apply.length > MAX_APPLY) {
  log(`${plan.apply.length - MAX_APPLY} picked item(s) over the cap of ${MAX_APPLY}, left for the next run: ${plan.apply.slice(MAX_APPLY).map(x => x.title).join('; ')}`)
}
log(`Plan: ${picked.length} to apply, ${plan.later.length} later, ${plan.big.length} big, ${plan.ask.length} for the user, ${plan.exceptions.length} exceptions, ${plan.rejected.length} rejected`)

phase('Apply')
const results = []
let broken = false
for (let i = 0; i < picked.length; i += CHUNK) {
  const chunk = picked.slice(i, i + CHUNK)
  const r = await agent(applyPrompt(chunk, i), {
    ...OPUS, label: `apply ${i + 1}-${i + chunk.length}`, phase: 'Apply', schema: APPLY_SCHEMA,
  })
  if (!r) {
    broken = true
    log(`The apply step for items ${i + 1}-${i + chunk.length} did not finish: look at the SSMP working tree before anything else.`)
    break
  }
  results.push(...r.results)
}
const committed = results.filter(r => r.outcome === 'committed' && r.commit)
log(`${committed.length} commit(s) made`)

phase('Review')
let review = null
if (committed.length) {
  review = await agent(reviewPrompt(committed, picked), { ...OPUS, label: 'review', phase: 'Review', schema: REVIEW_SCHEMA })
  if (!review) log('The review did not finish: review the tidy commits before pushing them.')
}

return { since: SINCE, date: DATE, decisions, reports, plan, results, broken, review }
