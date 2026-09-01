window.wazap = {
  shareLocation: async function (riderId) {
    const pos = await new Promise((resolve, reject) => {
      if (!navigator.geolocation) {
        reject(new Error('Géolocalisation non supportée par le navigateur.'));
        return;
      }
      navigator.geolocation.getCurrentPosition(
        (p) => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude }),
        (e) => reject(new Error(e.message || 'Accès à la position refusé.')),
        { enableHighAccuracy: true, timeout: 10000, maximumAge: 0 }
      );
    });

    const response = await fetch('/api/riders/location', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ riderUserId: riderId, latitude: pos.latitude, longitude: pos.longitude })
    });

    if (!response.ok) {
      throw new Error('Erreur serveur : ' + response.status);
    }

    return pos;
  },

  setAvailability: async function (riderId, isAvailable) {
    const response = await fetch('/api/riders/' + riderId + '/availability', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ isAvailable: isAvailable })
    });

    if (!response.ok) {
      throw new Error('Erreur serveur : ' + response.status);
    }
  }
};
