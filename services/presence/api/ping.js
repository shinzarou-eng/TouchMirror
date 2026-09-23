module.exports = async function handler(req, res) {
    if (req.method !== "POST") {
        res.status(204).end();
        return;
    }
    try {
        const { id, v } = req.body ?? {};
        const okId = typeof id === "string" && /^[0-9a-f]{16,64}$/i.test(id);
        const okVer = typeof v === "string" && /^\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(v);
        if (!okId || !okVer) {
            res.status(400).end();
            return;
        }
        const hook = process.env.DISCORD_WEBHOOK_URL;
        if (hook) {
            await fetch(hook, {
                method: "POST",
                headers: { "content-type": "application/json" },
                body: JSON.stringify({ content: `${id.toLowerCase()}|${v}` }),
                signal: AbortSignal.timeout(4000),
            });
        }
    } catch {
    }
    res.status(204).end();
};
