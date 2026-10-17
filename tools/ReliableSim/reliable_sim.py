"""Randomized simulation of the reliable numbering in SSMP's UpdateManager, the 27c8e33 logic ("old") against the review
fixes ("new"), mirroring the C#.

Two ends exchange one update packet per tick (20 ms); some carry reliable data (numbered). Per direction, the link:
- delivers in order with a fixed delay, and loses a share of the unreliable packets (Steam's relay arrival 70-100%);
- stalls now and then for 3-26 s, one way or both: reliable messages wait and arrive after it, unreliable ones (sent
  with NoDelay) are dropped;
- after an end opens the link anew (15 s of nothing, then every 8 s), everything in flight and waiting is dropped both
  ways, what the other end sends before it hears of the new session is lost, and for 0-6 s while the new session finds
  its route, unreliable packets are dropped and reliable ones wait;
- some runs send reliable data rarely, and each end has idle stretches without any (a player lying in a cocoon);
- a few packets with reliable data cannot be read on arrival (the same every time they are sent).
After 85% of the run no new reliable data is sent, so that everything has time to arrive. A run that hears nothing for
30 s counts as a disconnect and is left out. Checks that every reliable payload (but the unreadable ones) is taken in
exactly once and in order, and counts the hold timeouts and what was sent again.
"""
import random
import sys
from collections import deque

sys.stdout.reconfigure(encoding="utf-8")

TICK = 20
MOST_KEPT = 8192
HOLD_AT_MOST = 35000
HELD_WAIT_STEP = 1000
SEND_AGAIN_AFTER = 2000
DOUBLINGS = 3
ASK_WHEN_BEHIND = 1000
ASK_AFTER_RENEWAL = 3000  # old only
RENEW_AFTER = 15000
RENEW_INTERVAL = 8000
CEILING = 30000
NUMBERED, TOOK_IN, ASKS = 1, 2, 4


def u16(x):
    return x & 0xFFFF


class Kept:
    __slots__ = ("number", "payload", "unreadable", "sent_at", "again")

    def __init__(self, number, payload, unreadable, now):
        self.number = number
        self.payload = payload
        self.unreadable = unreadable
        self.sent_at = now
        self.again = 0

    def due(self, now):
        return now - self.sent_at >= SEND_AGAIN_AFTER << min(self.again, DOUBLINGS)


class End:
    def __init__(self, name, mode):
        self.name = name
        self.mode = mode
        self.last_sent = 0
        self.kept = deque()
        self.asked = False
        self.again_on_next_ack = False
        self.again_now = False
        self.sent_again_at = -10 ** 9  # old
        self.took_in = mode == "new"  # old: the first numbered packet sets the baseline
        self.last_taken = 0
        self.other_sent = 0
        self.behind_since = None
        self.held = {}
        self.held_since = 0
        self.held_waited = 0
        self.ask_until = -10 ** 9  # old
        self.taken = []
        self.gave_up = 0
        self.dups = 0
        self.last_receive = 0
        self.renewed_at_quiet = -1
        self.counter = 0
        self.unreadable_sent = set()
        self.resent = 0
        self.sent_reliable = 0

    def flags_now(self, now):
        if self.mode == "new":
            flags = TOOK_IN
            if self.held or (self.behind_since is not None and now - self.behind_since >= ASK_WHEN_BEHIND):
                flags |= ASKS
        else:
            flags = TOOK_IN if self.took_in else 0
            if self.held or now < self.ask_until:
                flags |= ASKS
        return flags

    def send_again(self, now, out):
        if self.mode == "new":
            if not self.again_now and not self.asked:
                return
            everything = self.again_now
            self.again_now = False
            self.asked = False
            chosen = []
            for k in self.kept:
                if everything or k.due(now):
                    k.sent_at = now
                    k.again += 1
                    chosen.append(k)
            if not chosen:
                return
        else:
            if self.again_now:
                pass
            elif self.asked and now - self.sent_again_at >= SEND_AGAIN_AFTER:
                pass
            else:
                self.asked = False
                return
            self.again_now = False
            self.asked = False
            self.sent_again_at = now
            chosen = list(self.kept)
            if not chosen:
                return
        flags = self.flags_now(now) | NUMBERED
        for k in chosen:
            self.resent += 1
            out.append(dict(flags=flags, number=k.number, ack=self.last_taken, payload=k.payload,
                            unreadable=k.unreadable, reliable=True))

    def make(self, now, reliable, rng):
        out = []
        self.send_again(now, out)
        flags = self.flags_now(now)
        number = self.last_sent if self.mode == "new" else 0
        payload = None
        unreadable = False
        if reliable:
            self.last_sent = u16(self.last_sent + 1)
            number = self.last_sent
            flags |= NUMBERED
            self.counter += 1
            payload = (self.name, self.counter)
            unreadable = rng.random() < 0.001
            if unreadable:
                self.unreadable_sent.add(payload)
            self.sent_reliable += 1
            if len(self.kept) >= MOST_KEPT:
                self.kept.popleft()
            self.kept.append(Kept(number, payload, unreadable, now))
        out.append(dict(flags=flags, number=number, ack=self.last_taken, payload=payload, unreadable=unreadable,
                        reliable=reliable))
        return out

    def let_go(self, ack):
        while self.kept and u16(ack - self.kept[0].number) <= MOST_KEPT:
            self.kept.popleft()

    def take(self, now, p):
        if self.held and self.mode == "new":
            self.held_waited += min(now - self.last_receive, HELD_WAIT_STEP)
        self.last_receive = now
        self.renewed_at_quiet = -1
        if p["unreadable"] and self.mode == "old":
            return  # 27c8e33 dropped it before the numbering
        f = p["flags"]
        if f & TOOK_IN:
            self.let_go(p["ack"])
            if self.again_on_next_ack:
                self.again_on_next_ack = False
                self.again_now = True
        if f & ASKS:
            self.asked = True
        order = []
        if not f & NUMBERED:
            order.append(p)
        else:
            self.take_numbered(now, p, order)
        if self.mode == "new" and f & TOOK_IN:
            ahead = u16(p["number"] - self.other_sent)
            if 0 < ahead <= MOST_KEPT:
                self.other_sent = p["number"]
        if self.held and (self.held_waited if self.mode == "new" else now - self.held_since) > HOLD_AT_MOST:
            self.gave_up += 1
            self.take_all_held(order)
        if self.mode == "new":
            behind = u16(self.other_sent - self.last_taken)
            if behind == 0 or behind > MOST_KEPT:
                self.behind_since = None
            elif self.behind_since is None:
                self.behind_since = now
        for q in order:
            if q["payload"] is not None and not q["unreadable"]:
                self.taken.append(q["payload"])

    def take_numbered(self, now, p, order):
        n = p["number"]
        if not self.took_in:
            self.took_in = True
            self.last_taken = n
            order.append(p)
            return
        ahead = u16(n - self.last_taken)
        if ahead == 1:
            self.last_taken = n
            order.append(p)
            took = 0
            while u16(self.last_taken + 1) in self.held:
                self.last_taken = u16(self.last_taken + 1)
                order.append(self.held.pop(self.last_taken))
                took += 1
            if took and self.held and self.mode == "new":
                self.held_since = now
                self.held_waited = 0
            return
        if ahead == 0 or ahead > 0xFFFF - MOST_KEPT:
            self.dups += 1
            return
        if ahead > MOST_KEPT:
            raise AssertionError("numbering started over")
        if not self.held:
            self.held_since = now
            self.held_waited = 0
        self.held.setdefault(n, p)

    def take_all_held(self, order):
        last = self.last_taken
        for n, q in sorted(self.held.items(), key=lambda kv: u16(kv[0] - last)):
            self.last_taken = n
            order.append(q)
        self.held.clear()

    def renewal(self, now):
        self.again_on_next_ack = True
        if self.mode == "old":
            self.ask_until = now + ASK_AFTER_RENEWAL


def run(seed, mode, ticks=60000):
    rng = random.Random(seed)
    a = End("A", mode)
    b = End("B", mode)
    delay = rng.randint(2, 8)
    loss = rng.choice([0.0, 0.1, 0.3])
    rate = rng.choice([0.6, 0.2, 0.05, 0.01])
    flight = {"AB": deque(), "BA": deque()}
    waiting = {"AB": deque(), "BA": deque()}  # reliable messages the transport keeps until it can send them
    session = 0
    sender_session = {"A": 0, "B": 0}
    stall = {"AB": 0, "BA": 0}
    route_up_at = 0
    idle_until = {"A": 0, "B": 0}
    pending_other = []
    renewals = 0
    no_new_reliable_at = int(ticks * 0.85)
    for t in range(ticks):
        now = t * TICK
        if t < ticks * 0.8 and rng.random() < 0.0004:
            length = rng.randint(150, 1300)
            which = rng.choice(["AB", "BA", "both"])
            for d in (["AB", "BA"] if which == "both" else [which]):
                stall[d] = max(stall[d], t + length)
        for name in ("A", "B"):
            if t < ticks * 0.8 and rng.random() < 0.0003:
                idle_until[name] = max(idle_until[name], t + rng.randint(250, 1500))
        # sending
        for end, d in ((a, "AB"), (b, "BA")):
            reliable = t < no_new_reliable_at and t >= idle_until[end.name] and rng.random() < rate
            for p in end.make(now, reliable, rng):
                sess = sender_session[end.name]
                if sess != session:
                    continue  # sent on a session the other end has closed
                blocked = t < stall[d] or t < route_up_at
                if p["reliable"]:
                    if blocked or waiting[d]:
                        waiting[d].append((sess, p))
                        continue
                elif blocked or rng.random() < loss:
                    continue
                flight[d].append((t + delay, sess, p))
        # what waited goes once the way is open
        for d in ("AB", "BA"):
            if t >= stall[d] and t >= route_up_at:
                while waiting[d]:
                    sess, p = waiting[d].popleft()
                    if sess == session:
                        flight[d].append((t + delay, sess, p))
        # delivery
        for d, receiver in (("AB", b), ("BA", a)):
            q = flight[d]
            while q and q[0][0] <= t:
                _, sess, p = q.popleft()
                if sess == session:
                    receiver.take(now, p)
        # opening the link anew
        for end, name in ((a, "A"), (b, "B")):
            quiet = now - end.last_receive
            if quiet >= RENEW_AFTER and (end.renewed_at_quiet < 0 or quiet - end.renewed_at_quiet >= RENEW_INTERVAL):
                if quiet >= CEILING:
                    return dict(result="disconnect")
                end.renewed_at_quiet = quiet
                renewals += 1
                session += 1
                for d in ("AB", "BA"):
                    flight[d].clear()
                    waiting[d].clear()
                route_up_at = t + rng.randint(0, 300)
                sender_session[name] = session
                other = "B" if name == "A" else "A"
                pending_other.append((t + rng.randint(delay, delay + 100), other, session))
                end.renewal(now)
        for item in list(pending_other):
            when, who, sess = item
            if t >= when:
                if sess == session:
                    sender_session[who] = sess
                pending_other.remove(item)
    problems = []
    for receiver, sender in ((b, a), (a, b)):
        want = [(sender.name, i) for i in range(1, sender.counter + 1)
                if (sender.name, i) not in sender.unreadable_sent]
        got = receiver.taken
        if got != want:
            if len(set(got)) != len(got):
                problems.append(receiver.name + " took duplicates")
            lost = len(set(want) - set(got))
            if lost:
                problems.append("%s lost %d of %d" % (receiver.name, lost, len(want)))
            elif got != want:
                problems.append(receiver.name + " out of order")
        if receiver.gave_up:
            problems.append("%s gave up waiting %d time(s)" % (receiver.name, receiver.gave_up))
    return dict(result="; ".join(problems) or "ok", renewals=renewals, rate=rate, loss=loss,
                resent=a.resent + b.resent, sent=a.sent_reliable + b.sent_reliable,
                unreadable=len(a.unreadable_sent) + len(b.unreadable_sent))


def main():
    seeds = int(sys.argv[1]) if len(sys.argv) > 1 else 500
    for mode in ("old", "new"):
        connected = bad = lost_runs = gave_up_runs = 0
        renewed_connected = 0
        resent = sent = 0
        by_rate = {}
        examples = []
        for seed in range(seeds):
            r = run(seed, mode)
            if r["result"] == "disconnect":
                continue
            connected += 1
            if r["renewals"]:
                renewed_connected += 1
            resent += r["resent"]
            sent += r["sent"]
            if r["result"] != "ok":
                bad += 1
                lost_runs += "lost" in r["result"]
                gave_up_runs += "gave up" in r["result"]
                by_rate[r["rate"]] = by_rate.get(r["rate"], 0) + 1
                if len(examples) < 4:
                    examples.append((seed, r))
        print("%s: %d of %d runs stayed connected (%d of them opened the link anew); %d with problems "
              "(%d lost data, %d gave up waiting), by reliable rate %r; sent again %.1f%% of what was sent"
              % (mode, connected, seeds, renewed_connected, bad, lost_runs, gave_up_runs, by_rate,
                 100.0 * resent / max(sent, 1)))
        for seed, r in examples:
            print("   seed", seed, r)


if __name__ == "__main__":
    main()
