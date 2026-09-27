const L = tm.lang && tm.lang() === 'en' ? {
  active: 'session-log active — session events journal',
  devices: 'devices: ',
  detected: ' detected',
  conn: 'mirror connected: ',
  closed: 'mirror closed: ',
  manual: ' — manual',
  cut: ' — drop',
  mir: 'active mirror: ',
  slot: 'slot ',
  rec: 'recording ',
  recOn: 'started',
  recOff: 'stopped',
} : {
  active: 'session-log actif — journal des événements',
  devices: 'appareils: ',
  detected: ' détecté(s)',
  conn: 'miroir connecté: ',
  closed: 'miroir fermé: ',
  manual: ' — volontaire',
  cut: ' — coupure',
  mir: 'miroir actif: ',
  slot: 'slot ',
  rec: 'enregistrement ',
  recOn: 'démarré',
  recOff: 'arrêté',
};

tm.log(L.active);

tm.on('devices', d => {
  const n = d.detected ?? 0;
  tm.log(L.devices + n + L.detected);
});

tm.on('mirror.connected', d =>
  tm.log(L.conn + (d.name || d.serial) + ' (' + L.slot + d.slot + ')'));

tm.on('mirror.disconnected', d =>
  tm.log(L.closed + (d.name || d.serial) + (d.manual ? L.manual : L.cut)));

tm.on('mirror.active', d =>
  tm.log(L.mir + (d.name || L.slot + d.slot)));

tm.on('mirror.recording', d =>
  tm.log(L.rec + (d.recording ? L.recOn : L.recOff) + ' (' + L.slot + d.slot + ')'));
