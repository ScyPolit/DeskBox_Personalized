# Ontology: verification-loop

Ten objects plus five verification levels (the model chooses among only L2 / L3 / L4). The declaration is data; instance states are computed from facts.

## Objects

### goal
- States: open → achieved → abandoned → superseded
- Initial: open · terminal: achieved, abandoned, superseded
- Stored in: fold.goal + clear/goals/ (no new storage)
- Each transition emits: goal/set
  - open → achieved: started by **system** on goal_verified (guards: independent_verdict_support)
  - open → abandoned: started by **model** on goal_abandoned (guards: independent_verdict_written)
  - open → superseded: started by **model** on new_goal_set
- Fields: claim, done_criteria, promote_at_level(L0|L1|L2|L3|L4), revision
- Note: A first-class object (recorded in the goal document); status is derived and has no place in the declaration

### hypothesis
- States: proposed → alive → confirmed → refuted → superseded → retracted
- Initial: proposed · terminal: refuted, superseded, retracted
- Stored in: fold.hypotheses (recorded together with goal/set) (no new storage)
- Each transition emits: hypothesis/superseded
  - proposed → alive: started by **system** on evidence_appended
  - proposed → confirmed: started by **system** on promotion_threshold
  - alive → confirmed: started by **system** on promotion_threshold
  - confirmed → refuted: started by **system** on refuting_evidence
  - alive → refuted: started by **system** on refuting_evidence
  - confirmed → retracted: started by **human** on retraction_request (guards: human_retraction_decision)
  - proposed → refuted: started by **system** on refuting_evidence
  - proposed → superseded: started by **model** on hypothesis_revised
  - alive → superseded: started by **model** on hypothesis_revised
- Fields: claim, refute_when
- Note: Status is computed from evidence: **the confirmed edge is recorded on the fact object** (promoted to a fact, so the panel reads it as confirmed); the judgment itself has no such change. refuted and retracted are sticky terminal states, and retracted is **a person's act**, recorded on the fact object (`fact/retracted`); status is not declared

### plan
- States: active → closed
- Initial: active · terminal: closed
- Stored in: fold.plans + clear/goals/plans/ (no new storage)
- Each transition emits: plan/created
  - active → closed: started by **model** on plan_closed
  - active → active: started by **system** on gate_failed_repeatedly (guards: block_threshold)
- Fields: brief, steps, confirmed_at, confirmed_by(user|autonomy|progress), blocked
- Note: "Blocked" is a field, not a state (it gets cleared); the authorization mark is a field too: a mark or earlier progress both count as authorization

### step
- States: open → advanced → void
- Initial: open · terminal: advanced, void
- Stored in: fold.plans[].steps (no new storage)
- Each transition emits: step/advanced
  - open → advanced: started by **system** on step_delivered (guards: admission_passed, level_judge_split, external_source_for_l4, human_release_for_l4)
  - open → void: started by **model** on step_voided
- Fields: do, done_criteria, artifacts, tests
- Note: A step is also one verification (step.tests = hypothesis + level); there is no "downgrade and redo" edge; "blocked" is not a step state but a field on the plan (see the plan self-loop)

### observation
- States: received → accepted → rejected
- Initial: received · terminal: accepted, rejected
- Stored in: fold.materials (accepted) + intake ledger (rejected) (no new storage)
- Each transition emits: observation/recorded
  - received → accepted: started by **system** on source_admissible (guards: admission_passed)
  - received → rejected: started by **system** on source_not_admissible
- Fields: content, source(self), ref
- Note: `received` lives inside one delivery call (a candidate observation): only accepted ones become objects; a rejected one leaves just an intake fact, a line in the ledger rather than a terminal state. Source values match their producers **one to one**: the type declares only what can actually happen today

### evaluation
- States: recorded
- Initial: recorded · terminal: recorded
- Stored in: fold.audits + clear/evidence/audits/ (no new storage)
- Each transition emits: audit/settled
- Fields: evaluator(self|independent|machine), verdict(support|refute|inconclusive), basis, shortfalls
- Note: Never changed once written; a re-evaluation adds a new one. Who works and who judges are split by level (from L3 up, only an independent evaluator writes)

### evidence
- States: recorded
- Initial: recorded · terminal: recorded
- Stored in: fold.evidence (no new storage)
- Each transition emits: evidence/recorded
- Fields: verdict(support|refute|inconclusive), level(L0|L1|L2|L3|L4), refs, anchor(auditor|artifact)
- Note: Kept apart from the step's own record; anchor says what the evidence is pinned to (an independent verdict or an output)

### fact
- States: promoted
- Initial: promoted · terminal: promoted
- Stored in: clear/knowledge/facts/ (no new storage)
- Each transition emits: fact/promoted
- Fields: text, scope, level, evidence, path, last_verified, refuted, review
- Note: Promotion is done by the system; each fact carries a boundary (scope) and a level, and the boundary is checked before citing it as known. The shelf is clear/knowledge/facts/INDEX.md (the panel reads the same table). Refutation only **marks** a fact (refuted, derived); retraction is **a person's act** (retracted, recorded as fact/retracted). They are kept apart because the data itself can be wrong

### anomaly
- States: open → explained → ruled_out → escalated
- Initial: open · terminal: explained, ruled_out, escalated
- Stored in: fold.anomalies (no new storage)
- Each transition emits: anomaly/opened
  - open → explained: started by **model** on anomaly_explained
  - open → ruled_out: started by **model** on anomaly_ruled_out
  - open → escalated: started by **model** on anomaly_escalated
- Fields: what, anchor, by(model|evaluator), reason
- Note: 认识论里的「反常」:和预期或本体对不上的观测。评估者报的随 audit/settled 一起来(by=evaluator)。不阻塞结案,但结案时随交付交给评估者

### release
- States: granted
- Initial: granted · terminal: granted
- Stored in: fold.releases (the native approval stack is the authoritative record) (no new storage)
- Each transition emits: human/released
- Fields: step, call, via(approval)
- Note: A person's release for L4: one release per delivery; once the step-level fact lands, retrying the same step does not ask again

## Five levels
- **L0** judge self · sources self · gate none: 旧账:读作自己检验(与 L2 合并,新写的 L0 当 L2 收)
- **L1** judge self · sources self · gate none: 旧账:读作自己检验(与 L2 合并,新写的 L1 当 L2 收)
- **L2** judge self · sources self · gate none: 自己检验:推理、已有知识、已有数据或小规模计算;依据必须可复查
- **L3** judge independent · sources self · gate registered_criteria: Newly produced, re-runnable evidence; criteria written before the work, verdict written by an independent evaluator
- **L4** judge independent · sources human_upload, file_drop, callback, pull · gate human_release: Unrepeatable or external evidence; criteria registered first, released by a person (the native approval stack is the authoritative record)
