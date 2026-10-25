// Selene's "Send report": takes a report from Selene Core and opens a GitHub issue with it, so players can report a
// problem without a GitHub account. Secrets and bindings (see wrangler.toml): GITHUB_TOKEN (fine-grained, Issues
// read/write on the repo only), REPO ("owner/name"), LIMITS (KV namespace for the rate limits).

const CATEGORIES = [
  "Something doesn't work",
  "Something looks wrong",
  "Selene froze or closed",
  "Controller or navigation",
  "Sound or music",
  "Other",
];
const MAX_REQUEST = 400_000;      // bytes, logs included
const MAX_BODY = 60_000;          // GitHub allows 65,536 characters in an issue body
const PER_IP_HOUR = 3;
const PER_DAY = 60;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (request.method === "GET") return text("Selene report service", 200);
    if (request.method !== "POST" || url.pathname !== "/report") return text("Not found", 404);

    const length = Number(request.headers.get("content-length") || 0);
    if (length > MAX_REQUEST) return json({ ok: false, error: "too big" }, 413);
    let report;
    try {
      const raw = await request.text();
      if (raw.length > MAX_REQUEST) return json({ ok: false, error: "too big" }, 413);
      report = JSON.parse(raw);
    } catch {
      return json({ ok: false, error: "bad request" }, 400);
    }
    if (!report || typeof report !== "object" || !CATEGORIES.includes(report.category)) return json({ ok: false, error: "bad request" }, 400);

    // a few reports per address an hour, and a daily ceiling for the whole service
    const ip = request.headers.get("cf-connecting-ip") || "unknown";
    const ipKey = "ip:" + (await sha256(ip)).slice(0, 32);
    const dayKey = "day:" + new Date().toISOString().slice(0, 10);
    const [ipCount, dayCount] = await Promise.all([env.LIMITS.get(ipKey), env.LIMITS.get(dayKey)]);
    if (Number(ipCount || 0) >= PER_IP_HOUR || Number(dayCount || 0) >= PER_DAY) return json({ ok: false, error: "slow down" }, 429);

    const issue = build(report);
    const response = await fetch(`https://api.github.com/repos/${env.REPO}/issues`, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${env.GITHUB_TOKEN}`,
        Accept: "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "selene-report-worker",
        "Content-Type": "application/json",
      },
      body: JSON.stringify(issue),
    });
    if (!response.ok) {
      console.log("github", response.status, await response.text());
      return json({ ok: false, error: "github" }, 502);
    }
    const created = await response.json();
    await Promise.all([
      env.LIMITS.put(ipKey, String(Number(ipCount || 0) + 1), { expirationTtl: 3600 }),
      env.LIMITS.put(dayKey, String(Number(dayCount || 0) + 1), { expirationTtl: 172800 }),
    ]);
    return json({ ok: true, number: created.number, url: created.html_url }, 200);
  },
};

function build(r) {
  const field = (v, max) => scrub(String(v ?? "").slice(0, max)).trim();
  const note = field(r.note, 2000);
  const lines = [
    `**${r.category}**${field(r.place, 60) ? "  ·  " + field(r.place, 60) : ""}`,
    "",
    note ? note : "_No description given._",
    "",
    "| | |",
    "|---|---|",
    `| Selene | ${cell(field(r.selene, 40))} |`,
    `| Playnite | ${cell(field(r.playnite, 40))} |`,
    `| Windows | ${cell(field(r.windows, 80))} |`,
    "",
    "_Sent from Selene's Report a problem screen._",
  ];
  let body = lines.join("\n");
  // the logs fill what's left, newest lines kept
  const logs = [["Problems in selene.log", r.seleneProblems], ["selene.log (recent)", r.seleneLog], ["playnite.log (warnings and errors)", r.playniteLog]].filter(([, v]) => typeof v === "string" && v.length);
  const room = Math.max(0, MAX_BODY - body.length - 400);
  const each = logs.length ? Math.floor(room / logs.length) : 0;
  for (const [name, content] of logs) {
    let log = scrub(content).replace(/````/g, "'''");
    if (log.length > each) log = "(older lines cut)\n" + log.slice(log.length - each + 20);
    body += `\n\n<details><summary>${name}</summary>\n\n\`\`\`\`\n${log}\n\`\`\`\`\n</details>`;
  }
  const short = note.replace(/\s+/g, " ").slice(0, 60);
  const where = field(r.place, 60);
  return { title: `[Report] ${r.category}${where ? " (" + where + ")" : ""}${short ? ": " + short : ""}`, body, labels: ["bug"] };
}

// no names or home folders in a public issue
function scrub(s) {
  return s
    .replace(/([A-Za-z]:\\Users\\)[^\\\/\r\n"']+/gi, "$1<user>")
    .replace(/([A-Za-z]:\/Users\/)[^\\\/\r\n"']+/gi, "$1<user>")
    .replace(/[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g, "<email>");
}

function cell(s) {
  return (s || "-").replace(/\|/g, "/").replace(/\r?\n/g, " ");
}

async function sha256(s) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(s));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function json(obj, status) {
  return new Response(JSON.stringify(obj), { status, headers: { "Content-Type": "application/json" } });
}

function text(s, status) {
  return new Response(s, { status, headers: { "Content-Type": "text/plain" } });
}
