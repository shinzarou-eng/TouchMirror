const token = process.env.DISCORD_BOT_TOKEN;
const channel = process.env.CH_PINGS;
const now = Date.now();
const windowMs = 20 * 60 * 1000;
const purgeMs = 2 * 60 * 60 * 1000;

if (!token || !channel) {
    console.log("online=");
    process.exit(0);
}

const api = `https://discord.com/api/v10/channels/${channel}/messages`;
const ids = new Set();
const stale = [];
let before = "";
let oldest = 0;

for (let page = 0; page < 10; page++) {
    const url = before ? `${api}?limit=100&before=${before}` : `${api}?limit=100`;
    const res = await fetch(url, { headers: { authorization: `Bot ${token}` } });
    if (!res.ok) break;
    const msgs = await res.json();
    if (!Array.isArray(msgs) || msgs.length === 0) break;
    for (const m of msgs) {
        const ts = Date.parse(m.timestamp);
        const age = now - ts;
        if (age <= windowMs && typeof m.content === "string")
            ids.add(m.content.split("|")[0]);
        if (age > purgeMs)
            stale.push(m.id);
        oldest = ts;
    }
    before = msgs[msgs.length - 1].id;
    if (msgs.length < 100 || now - oldest > windowMs) break;
}

for (const id of stale.slice(0, 200)) {
    try {
        await fetch(`${api}/${id}`, {
            method: "DELETE",
            headers: { authorization: `Bot ${token}` },
        });
    } catch {
    }
}

console.log(`online=${ids.size}`);
