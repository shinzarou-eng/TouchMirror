const armed = {};

tm.log('hud actif — courbe FPS + métriques sur les miroirs');

tm.setInterval(() => {
  const mirrors = tm.getMirrors().data || [];
  for (const m of mirrors) {
    if (!m.connected) continue;
    if (!armed[m.slot]) {
      tm.overlay(m.slot, { visible: true, title: 'hud', pos: 'tr', color: '#3ECF8E' });
      armed[m.slot] = true;
    }
    tm.overlay(m.slot, { title: m.w ? m.w + '×' + m.h : 'hud' });
    const lag = Math.max(0, Math.round(m.lag || 0));
    const jit = Math.round(m.jit || 0);
    const mbps = m.bitrate ? ' · ' + (m.bitrate / 1e6).toFixed(0) + ' M' : '';
    tm.push(m.slot, { value: m.fps, label: Math.round(m.fps) + ' fps · +' + lag + ' ms' + mbps });
  }
  const live = mirrors.map(m => m.slot);
  for (const s of Object.keys(armed))
    if (!live.includes(+s)) delete armed[s];
}, 1000);
