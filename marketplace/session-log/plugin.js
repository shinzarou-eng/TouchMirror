tm.log('session-log actif — journal des événements');

tm.on('devices', d => {
  const n = d.detected ?? 0;
  tm.log('appareils: ' + n + ' détecté(s)');
});

tm.on('mirror.connected', d =>
  tm.log('miroir connecté: ' + (d.name || d.serial) + ' (slot ' + d.slot + ')'));

tm.on('mirror.disconnected', d =>
  tm.log('miroir fermé: ' + (d.name || d.serial) + (d.manual ? ' — volontaire' : ' — coupure')));

tm.on('mirror.active', d =>
  tm.log('miroir actif: ' + (d.name || 'slot ' + d.slot)));

tm.on('mirror.recording', d =>
  tm.log('enregistrement ' + (d.recording ? 'démarré' : 'arrêté') + ' (slot ' + d.slot + ')'));
