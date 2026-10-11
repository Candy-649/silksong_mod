using System.Text.RegularExpressions;

// --events <event name regex>: prints every FSM that changes state on a matching event - by a global transition or by
// one of a state's own - with the state each goes to and what that state does, so that everything in the game that
// answers one event can be listed at once. FSMs that answer the same way are printed together, with where each is.
internal static class EventScan {
    public static void Print(ScanResult scan, Regex eventPattern) {
        foreach (var error in scan.Errors) {
            Console.WriteLine($"error: {error}");
        }

        var groups = new Dictionary<string, (List<string> Lines, List<string> Places)>(StringComparer.Ordinal);
        foreach (var record in scan.Fsms) {
            var states = record.States ?? [];
            var matched = new List<(string From, string Event, string To)>();
            foreach (var transition in record.GlobalTransitions ?? []) {
                var (eventName, toState) = Split(transition);
                if (eventPattern.IsMatch(eventName)) {
                    matched.Add(("(global)", eventName, toState));
                }
            }

            foreach (var state in states) {
                foreach (var transition in state.Transitions) {
                    var (eventName, toState) = Split(transition);
                    if (eventPattern.IsMatch(eventName)) {
                        matched.Add((state.Name, eventName, toState));
                    }
                }
            }

            if (matched.Count == 0) {
                continue;
            }

            // What the FSM is and what it does on the event: the same for every object built from one prefab
            var lines = new List<string> {
                $"fsm {record.FsmName}" + (record.TemplateName != null ? $" (template {record.TemplateName})" : "") +
                $", start {record.StartState}"
            };
            foreach (var (from, eventName, toState) in matched) {
                lines.Add($"  [{from}] {eventName} -> {toState}");
            }

            // The states the event leads to, and the ones they go on to, a few steps deep
            var byName = states.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First());
            var shown = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<(string Name, int Depth)>(matched.Select(m => (m.To, 0)));
            while (queue.Count > 0) {
                var (name, depth) = queue.Dequeue();
                if (!shown.Add(name) || !byName.TryGetValue(name, out var state)) {
                    continue;
                }

                var actions = string.Join(", ", state.Actions.Select(a => a[(a.LastIndexOf('.') + 1)..]));
                var strings = state.Strings.Count > 0 ? $" | strings: {string.Join(", ", state.Strings.Take(8))}" : "";
                lines.Add($"    {name}: {actions} | -> {string.Join("; ", state.Transitions)}{strings}");
                if (depth < 2) {
                    foreach (var transition in state.Transitions) {
                        queue.Enqueue((Split(transition).ToState, depth + 1));
                    }
                }
            }

            var key = string.Join("\n", lines);
            if (!groups.TryGetValue(key, out var group)) {
                group = (lines, new List<string>());
                groups[key] = group;
            }

            group.Places.Add($"{record.Scene ?? record.Bundle}: {record.ObjectPath}");
        }

        Console.WriteLine($"== FSMs that change state on events matching {eventPattern}: {groups.Count} kinds, " +
                          $"{groups.Values.Sum(g => g.Places.Count)} FSMs ==");
        foreach (var (lines, places) in groups.Values.OrderBy(g => g.Places.Min(StringComparer.Ordinal))) {
            Console.WriteLine();
            Console.WriteLine($"-- {places.Count} x " + lines[0]);
            foreach (var line in lines.Skip(1)) {
                Console.WriteLine(line);
            }

            foreach (var place in places.Distinct().OrderBy(p => p, StringComparer.Ordinal).Take(12)) {
                Console.WriteLine($"    at {place}");
            }

            if (places.Distinct().Count() > 12) {
                Console.WriteLine($"    ... and {places.Distinct().Count() - 12} more");
            }
        }
    }

    private static (string Event, string ToState) Split(string transition) {
        var arrow = transition.LastIndexOf("->", StringComparison.Ordinal);
        return arrow < 0 ? (transition, "?") : (transition[..arrow], transition[(arrow + 2)..]);
    }
}
