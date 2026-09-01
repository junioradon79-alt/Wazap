window.wazap = {
  saveToken: function (token) {
    localStorage.setItem('wazap_token', token);
  },
  clearToken: function () {
    localStorage.removeItem('wazap_token');
  },

  shareLocation: async function () {
    const token = localStorage.getItem('wazap_token');
    if (!token) throw new Error('Non authentifié.');

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
      headers: {
        'Content-Type': 'application/json',
        'Authorization': 'Bearer ' + token
      },
      body: JSON.stringify({ latitude: pos.latitude, longitude: pos.longitude })
    });

    if (!response.ok) {
      throw new Error('Erreur serveur : ' + response.status);
    }

    return pos;
  },

  setAvailability: async function (riderId, isAvailable) {
    const token = localStorage.getItem('wazap_token');
    if (!token) throw new Error('Non authentifié.');

    const response = await fetch('/api/riders/' + riderId + '/availability', {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': 'Bearer ' + token
      },
      body: JSON.stringify({ isAvailable: isAvailable })
    });

    if (!response.ok) {
      throw new Error('Erreur serveur : ' + response.status);
    }
  }
};
