using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

/// <summary>One column of the page: clips played one after another, each frame with how long it shows.</summary>
internal sealed class Run {
    public string Title;
    public readonly List<Part> Parts = new();

    public float Seconds => Parts.Sum(part => part.Seconds);

    /// <summary>A clip as a run plays it: the frames it shows, in order, a frame that stays being one entry.</summary>
    public sealed class Part {
        public Clip Clip;
        public string How, Note;
        public readonly List<(int Frame, float Seconds)> Shown = new();

        public float Seconds => Shown.Sum(shown => shown.Seconds);

        public void Add(int frame, float seconds) {
            if (Shown.Count > 0 && Shown[^1].Frame == frame) {
                Shown[^1] = (frame, Shown[^1].Seconds + seconds);
            } else {
                Shown.Add((frame, seconds));
            }
        }
    }

    public static Run Parse(string text, Dictionary<string, Clip> clips) {
        var colon = text.IndexOf(':');
        var run = new Run { Title = text[..colon] };
        var steps = text[(colon + 1)..].Split(';', StringSplitOptions.TrimEntries);
        foreach (var step in steps.Where(step => step != "")) {
            var equals = step.IndexOf('=');
            var spec = equals < 0 ? step : step[..equals];
            var part = new Part { Note = equals < 0 ? null : step[(equals + 1)..] };
            var at = spec.LastIndexOf('@');
            var tilde = spec.LastIndexOf('~');
            part.Clip = clips[(at > 0 ? spec[..at] : tilde > 0 ? spec[..tilde] : spec).Trim()];
            var frameTime = 1 / part.Clip.Fps;
            if (tilde > 0) {
                var seconds = float.Parse(spec[(tilde + 1)..], CultureInfo.InvariantCulture);
                part.How = $"放 {seconds:0.###} 秒";
                var time = 0f;
                for (var i = 0; time < seconds - 1e-4f; i++) {
                    part.Add(FrameAt(part.Clip, i), Math.Min(frameTime, seconds - time));
                    time += frameTime;
                }
            } else {
                var last = at > 0 ? int.Parse(spec[(at + 1)..]) : part.Clip.Frames.Count - 1;
                part.How = at > 0 ? $"放到第 {last} 帧" : "放完";
                for (var i = 0; i <= last; i++) {
                    part.Add(i, frameTime);
                }
            }

            run.Parts.Add(part);
        }

        return run;
    }

    /// <summary>The frame a clip shows as its i-th, going round or holding its last frame as it wraps.</summary>
    private static int FrameAt(Clip clip, int i) {
        var count = clip.Frames.Count;
        return clip.WrapMode switch {
            0 => i % count,
            1 => i < count ? i : clip.LoopStart + (i - clip.LoopStart) % (count - clip.LoopStart),
            6 => 0,
            _ => Math.Min(i, count - 1)
        };
    }
}

/// <summary>
/// The page: the runs played side by side at the game's speed, slower or a frame at a time, and every frame of each
/// below, with the frames that fire their clip's event marked.
/// </summary>
internal static class Page {
    public static void Write(string path, string title, string intro, List<Run> runs) {
        var sprites = runs.SelectMany(run => run.Parts)
            .SelectMany(part => part.Shown.Select(shown => part.Clip.Frames[shown.Frame].Sprite))
            .Distinct()
            .ToList();
        var pngs = Draw.Pngs(sprites, out var width, out var height);
        var indexOf = sprites.Select((sprite, i) => (sprite, i)).ToDictionary(pair => pair.sprite, pair => pair.i);
        var images = sprites.Select(sprite => "data:image/png;base64," + Convert.ToBase64String(pngs[sprite]));

        var data = runs.Select(run => new {
            title = run.Title,
            total = run.Seconds,
            shown = run.Parts.SelectMany(part => part.Shown.Select(shown => new object[] {
                indexOf[part.Clip.Frames[shown.Frame].Sprite], shown.Seconds, $"{part.Clip.Name} 第 {shown.Frame} 帧"
            }))
        });

        var players = new StringBuilder();
        var strips = new StringBuilder();
        foreach (var run in runs) {
            players.Append($"""
                <div class="player"><h2>{H(run.Title)}</h2><div class="stage"><img alt=""></div>
                <p class="now"></p><p class="time"></p></div>
                """);
            strips.Append($"<section><h2>{H(run.Title)}：共 {run.Seconds:0.00} 秒</h2>");
            foreach (var part in run.Parts) {
                var clip = part.Clip;
                strips.Append($"""
                    <h3>{H(clip.Name)}</h3><p class="meta">{part.How}：这段 {part.Seconds:0.00} 秒（整段 {clip.Frames.Count} 帧，
                    每秒 {clip.Fps:0.##} 帧）{(part.Note == null ? "" : "。" + H(part.Note))}</p><div class="strip">
                    """);
                foreach (var (frame, seconds) in part.Shown) {
                    var fired = clip.Frames[frame].Trigger ? "<b>触发</b>" : "";
                    strips.Append($"""
                        <figure><img data-i="{indexOf[clip.Frames[frame].Sprite]}" alt="">
                        <figcaption>第 {frame} 帧 · {seconds:0.00}s {fired}</figcaption></figure>
                        """);
                }

                strips.Append("</div>");
            }

            strips.Append("</section>");
        }

        var html = Template
            .Replace("{{title}}", H(title))
            .Replace("{{intro}}", H(intro))
            .Replace("{{width}}", width.ToString(CultureInfo.InvariantCulture))
            .Replace("{{height}}", height.ToString(CultureInfo.InvariantCulture))
            .Replace("{{players}}", players.ToString())
            .Replace("{{strips}}", strips.ToString())
            .Replace("{{images}}", JsonSerializer.Serialize(images))
            .Replace("{{runs}}", JsonSerializer.Serialize(data));
        File.WriteAllText(path, html, new UTF8Encoding(false));
    }

    private static string H(string text) => WebUtility.HtmlEncode(text);

    private const string Template = """
        <!doctype html>
        <html lang="zh-CN">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{title}}</title>
        <style>
        :root { --bg: #f3f1ec; --panel: #e7e3da; --fg: #24221e; --muted: #6e695f; --accent: #a8432a;
          --stage: #9a958b; color-scheme: light }
        @media (prefers-color-scheme: dark) {
          :root { --bg: #1c1b19; --panel: #282622; --fg: #ebe7df; --muted: #a59f94; --accent: #e07a5f;
            --stage: #45423d; color-scheme: dark }
        }
        body { margin: 0; padding: 24px 16px 48px; background: var(--bg); color: var(--fg);
          font: 15px/1.6 system-ui, "Microsoft YaHei", sans-serif }
        main { max-width: 1200px; margin: 0 auto; display: grid; gap: 20px }
        h1 { font-size: 22px; margin: 0 }
        h2 { font-size: 17px; margin: 0 0 8px }
        h3 { font-size: 15px; margin: 18px 0 2px }
        p { margin: 0 }
        .intro, .meta, .time { color: var(--muted) }
        .players { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 16px }
        .player { background: var(--panel); border-radius: 8px; padding: 12px; min-width: 0 }
        .stage { background: var(--stage); border-radius: 4px; aspect-ratio: {{width}} / {{height}};
          display: grid; place-items: center; overflow: hidden }
        .stage img { width: 100%; height: auto; display: block }
        .now { font-weight: 600; margin-top: 8px; font-variant-numeric: tabular-nums }
        .time { font-variant-numeric: tabular-nums }
        .controls { display: flex; flex-wrap: wrap; gap: 8px; align-items: center }
        button { font: inherit; padding: 4px 12px; border-radius: 6px; border: 1px solid var(--muted);
          background: var(--panel); color: var(--fg); cursor: pointer }
        button.on { border-color: var(--accent); color: var(--accent); font-weight: 600 }
        input[type=range] { flex: 1 1 200px; accent-color: var(--accent) }
        .strip { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 6px }
        figure { margin: 0; width: 132px; background: var(--panel); border-radius: 4px; padding: 4px }
        figure img { width: 100%; height: auto; display: block; background: var(--stage); border-radius: 2px }
        figcaption { font-size: 12px; color: var(--muted); font-variant-numeric: tabular-nums }
        figcaption b { color: var(--accent) }
        section { border-top: 1px solid var(--panel); padding-top: 12px }
        </style>
        </head>
        <body>
        <main>
        <h1>{{title}}</h1>
        <p class="intro">{{intro}}</p>
        <div class="controls">
          <button data-speed="1" class="on">原速</button><button data-speed="0.5">慢 2 倍</button>
          <button data-speed="0.25">慢 4 倍</button><button id="pause">暂停</button>
          <button id="back">上一帧</button><button id="next">下一帧</button>
          <input id="seek" type="range" min="0" step="0.001">
        </div>
        <div class="players">{{players}}</div>
        {{strips}}
        </main>
        <script>
        const images = {{images}};
        const runs = {{runs}};
        const players = [...document.querySelectorAll('.player')];
        const total = Math.max(...runs.map(run => run.total));
        const seek = document.getElementById('seek');
        seek.max = total;
        const edges = [...new Set(runs.flatMap(run => {
          let at = 0;
          return run.shown.map(shown => (at += shown[1]));
        }).concat([0]).map(t => Math.round(t * 1000) / 1000))].sort((a, b) => a - b);
        let speed = 1, playing = true, time = 0, last = performance.now();

        document.querySelectorAll('img[data-i]').forEach(img => { img.src = images[img.dataset.i]; });

        function shownAt(run, t) {
          let at = 0;
          for (const shown of run.shown) {
            at += shown[1];
            if (t < at - 1e-6) return shown;
          }
          return run.shown[run.shown.length - 1];
        }

        function show() {
          const t = Math.min(time, total);
          runs.forEach((run, k) => {
            const shown = shownAt(run, t), player = players[k], img = player.querySelector('.stage img');
            if (img.dataset.at !== String(shown[0])) { img.src = images[shown[0]]; img.dataset.at = shown[0]; }
            player.querySelector('.now').textContent = t >= run.total ? shown[2] + '（已放完，停在最后一帧）' : shown[2];
            player.querySelector('.time').textContent =
              Math.min(t, run.total).toFixed(2) + ' / ' + run.total.toFixed(2) + ' 秒';
          });
          seek.value = t;
        }

        function tick(now) {
          if (playing) {
            time += (now - last) / 1000 * speed;
            if (time > total + 0.8) time = 0;
          }
          last = now;
          show();
          requestAnimationFrame(tick);
        }

        function pause() {
          playing = false;
          document.getElementById('pause').textContent = '播放';
        }

        document.querySelectorAll('button[data-speed]').forEach(button => button.addEventListener('click', () => {
          speed = Number(button.dataset.speed);
          document.querySelectorAll('button[data-speed]')
            .forEach(other => other.classList.toggle('on', other === button));
        }));
        document.getElementById('pause').addEventListener('click', event => {
          playing = !playing;
          event.target.textContent = playing ? '暂停' : '播放';
        });
        document.getElementById('next').addEventListener('click', () => {
          pause();
          time = edges.find(edge => edge > time + 1e-4) ?? 0;
        });
        document.getElementById('back').addEventListener('click', () => {
          pause();
          time = [...edges].reverse().find(edge => edge < time - 1e-4) ?? 0;
        });
        seek.addEventListener('input', () => {
          pause();
          time = Number(seek.value);
        });
        requestAnimationFrame(tick);
        </script>
        </body>
        </html>
        """;
}
