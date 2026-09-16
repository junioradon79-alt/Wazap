using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Domain.Enums;
using Wazap.Domain.Services;

namespace Wazap.Application.Services
{
    /// <summary>
    /// Sélection des livreurs à qui une offre peut être proposée (P2 / C-13 : extrait de
    /// <see cref="DeliveryOfferService"/>, qui portait 1 078 lignes).
    ///
    /// Le calcul est isolé parce que c'est la partie la plus délicate du produit : les exclusions
    /// (liste noire, sinistre en cours d'enquête), la certification obligatoire, le filtre de
    /// réputation, la compatibilité de zone et la priorité payante sont traduits **en SQL** ; une
    /// erreur de traduction écarte des livreurs éligibles (aucune course n'est proposée) ou en
    /// retient qu'il ne fallait pas. Les règles de tri sont exposées en <c>public static</c> pour
    /// être testées sans base.
    ///
    /// Ce service ne décide de rien concernant l'argent : il ne fait que proposer des candidats.
    ///
    /// ⚠️ Il est construit par son propriétaire (<see cref="DeliveryOfferService"/>) à partir des
    /// options déjà injectées, plutôt que par le conteneur : ses dépendances sont exactement
    /// celles de son appelant, et l'ajouter comme paramètre de constructeur aurait imposé de
    /// modifier les neuf sites de construction existants (dont huit tests) sans rien apporter.
    /// </summary>
    public sealed class RiderMatchingService
    {
        private readonly IApplicationDbContext _context;
        private readonly GeoOptions _geo;
        private readonly RiderSecurityOptions _riderSecurity;
        private readonly RiderReputationOptions _reputation;
        private readonly RiderPriorityOptions _priority;

        public RiderMatchingService(
            IApplicationDbContext context,
            GeoOptions geo,
            RiderSecurityOptions riderSecurity,
            RiderReputationOptions reputation,
            RiderPriorityOptions priority)
        {
            _context = context;
            _geo = geo;
            _riderSecurity = riderSecurity;
            _reputation = reputation;
            _priority = priority;
        }

        /// <summary>
        /// Boîte englobante (degrés) du rayon de diffusion, <b>garantie contenir le cercle</b> :
        /// la marge en longitude est calculée à la latitude la plus HAUTE de la boîte (cas le
        /// plus défavorable, où un degré de longitude est le plus court), ce qui évite d'écarter
        /// un livreur réellement dans le rayon. Le filtre Haversine reste appliqué ensuite.
        /// Près des pôles (ou si la boîte franchit l'antiméridien) on ne filtre pas : mieux vaut
        /// charger quelques candidats de trop que d'en perdre un.
        /// </summary>
        public static (double MinLat, double MaxLat, double MinLon, double MaxLon) BoundingBox(
            double latitude, double longitude, double radiusKm)
        {
            const double kmPerDegreeLatitude = 111.32;
            const double fullRange = 180.0;

            var deltaLat = radiusKm / kmPerDegreeLatitude;

            if (Math.Abs(latitude) + deltaLat >= 89.0)
                return (-90, 90, -fullRange, fullRange);

            var worstLatitude = Math.Min(89.0, Math.Abs(latitude) + deltaLat);
            var cosinus = Math.Max(0.01, Math.Cos(worstLatitude * Math.PI / 180.0));
            var deltaLon = radiusKm / (kmPerDegreeLatitude * cosinus);

            if (longitude - deltaLon < -fullRange || longitude + deltaLon > fullRange)
                return (latitude - deltaLat, latitude + deltaLat, -fullRange, fullRange);

            return (latitude - deltaLat, latitude + deltaLat, longitude - deltaLon, longitude + deltaLon);
        }

        /// <summary>Livreurs disponibles, partage activé, position fraîche (&lt; Geo:LocationFreshnessMinutes)
        /// et dans le rayon Geo:MaxDistanceKm, triés par distance (Haversine).
        /// </summary>
        public async Task<IReadOnlyList<NearestRiderDto>> FindNearestAsync(
            Guid vendorUserId,
            int count,
            IReadOnlyCollection<Guid> excludeRiderIds)
        {
            var vendor = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == vendorUserId)
                ?? throw new InvalidOperationException("Vendeur introuvable.");

            // Position GPS OU zone déclarée exigées (livraison à la demande, téléphones basiques).
            if ((vendor.Latitude is null || vendor.Longitude is null) && string.IsNullOrWhiteSpace(vendor.Zone))
                throw new InvalidOperationException("Le vendeur n'a ni position GPS ni zone déclarée.");

            var nowUtc = DateTime.UtcNow;
            var freshnessThreshold = nowUtc.AddMinutes(-_geo.LocationFreshnessMinutes);

            var exclude = excludeRiderIds?.ToHashSet() ?? new HashSet<Guid>();
            var byGps = new List<NearestRiderDto>();

            // Exclusions exprimées EN SQL (sous-requêtes EXISTS) : charger toutes les identités
            // blacklistées et tous les sinistres en cours pour les croiser en mémoire faisait
            // croître le coût du matching avec l'historique de la plateforme.
            var blacklisted = _context.RiderIdentities.AsNoTracking()
                .Where(i => i.Status == RiderIdentityStatus.Blacklisted);

            // Sinistre en cours d'enquête : le livreur est suspendu tant que le
            // dossier « Garantie Colis Sûr » n'est pas tranché.
            var underInvestigation = _context.DeliveryClaims.AsNoTracking()
                .Where(c => c.Status == DeliveryClaimStatus.Pending);

            // Réputation : écarte les livreurs sous la moyenne minimale. Désactivé par
            // défaut (MinimumAverageScore = 0) et jamais appliqué en dessous d'un nombre
            // suffisant d'avis — un nouveau livreur ne doit pas sortir du vivier sur une
            // seule mauvaise note. Ce filtre agrégé reste chargé en mémoire (il n'est actif
            // que si l'option est activée explicitement, et ne concerne que les notés).
            if (_reputation.MinimumAverageScore > 0)
            {
                var poorlyRated = await _context.RiderRatings.AsNoTracking()
                    .GroupBy(r => r.RiderUserId)
                    .Where(g => g.Count() >= _reputation.MinimumRatingsBeforeFiltering
                             && g.Average(r => r.Score) < _reputation.MinimumAverageScore)
                    .Select(g => g.Key)
                    .ToListAsync();
                exclude.UnionWith(poorlyRated);
            }

            // Tier 1 — GPS (Haversine) : uniquement si le vendeur a une position.
            if (vendor.Latitude is not null && vendor.Longitude is not null)
            {
                var latitude = vendor.Latitude.Value;
                var longitude = vendor.Longitude.Value;

                // Boîte englobante : réduit en SQL l'ensemble des candidats avant le calcul de
                // distance, au lieu de charger tous les livreurs géolocalisés de la plateforme.
                // Elle CONTIENT le cercle (marge de longitude calculée à la latitude la plus
                // haute de la boîte), donc aucun livreur éligible n'est écarté ; le filtre
                // Haversine ci-dessous reste l'arbitre.
                var box = BoundingBox(latitude, longitude, _geo.MaxDistanceKm);

                var gpsQuery = _context.Users.AsNoTracking()
                    .Where(u => u.Role == UserRole.Rider
                             && u.IsAvailable
                             && u.LocationSharingEnabled
                             && u.Latitude != null
                             && u.Longitude != null
                             && u.LocationUpdatedAt >= freshnessThreshold
                             && u.Latitude >= box.MinLat && u.Latitude <= box.MaxLat
                             && u.Longitude >= box.MinLon && u.Longitude <= box.MaxLon
                             && !blacklisted.Any(i => i.UserId == u.Id)
                             && !underInvestigation.Any(c => c.RiderUserId == u.Id));

                // Certification obligatoire (« Garantie Colis Sûr ») : seuls les livreurs dont
                // le dossier d'identité est Verified reçoivent des offres.
                if (_riderSecurity.RequireCertifiedRiders)
                {
                    gpsQuery = gpsQuery.Where(u => _context.RiderIdentities
                        .Any(i => i.UserId == u.Id && i.Status == RiderIdentityStatus.Verified));
                }

                var riders = await gpsQuery
                    .Select(u => new { u.Id, u.Latitude, u.Longitude, u.PriorityUntilUtc })
                    .ToListAsync();

                byGps.AddRange(riders
                    .Where(r => !exclude.Contains(r.Id))
                    .Select(r => new NearestRiderDto(
                        r.Id,
                        GeoDistance.DistanceKm(
                            latitude,
                            longitude,
                            r.Latitude.GetValueOrDefault(),
                            r.Longitude.GetValueOrDefault()),
                        r.PriorityUntilUtc))
                    .Where(x => x.DistanceKm <= _geo.MaxDistanceKm));
            }

            // Tier 2 (téléphones basiques sans GPS) : compléter avec les livreurs
            // dont la ZONE déclarée correspond à celle du vendeur — comparaison faite EN SQL.
            if (byGps.Count < count && !string.IsNullOrWhiteSpace(vendor.Zone))
            {
                var contacted = new HashSet<Guid>(exclude);
                contacted.UnionWith(byGps.Select(r => r.RiderUserId));

                var targetZone = vendor.Zone.Trim().ToLowerInvariant();

                var zoneQuery = _context.Users.AsNoTracking()
                    .Where(u => u.Role == UserRole.Rider
                             && u.IsAvailable
                             && u.LocationSharingEnabled
                             && u.Zone != null
                             && u.Zone.Trim().ToLower() == targetZone
                             && !blacklisted.Any(i => i.UserId == u.Id)
                             && !underInvestigation.Any(c => c.RiderUserId == u.Id));

                if (_riderSecurity.RequireCertifiedRiders)
                {
                    zoneQuery = zoneQuery.Where(u => _context.RiderIdentities
                        .Any(i => i.UserId == u.Id && i.Status == RiderIdentityStatus.Verified));
                }

                var zoneRiders = await zoneQuery
                    .Select(u => new { u.Id, u.PriorityUntilUtc })
                    .ToListAsync();

                byGps.AddRange(zoneRiders
                    .Where(r => !contacted.Contains(r.Id))
                    .Select(r => new NearestRiderDto(r.Id, double.MaxValue, r.PriorityUntilUtc)));
            }

            // Ordre final : pondération par réputation (optionnelle) OU strictement
            // géographique. Le tri par réputation charge les moyennes en une seule requête.
            if (byGps.Count > 1)
            {
                if (_reputation.PreferHigherRatedRiders)
                {
                    var scores = await LoadAverageScoresAsync(byGps.Select(r => r.RiderUserId).ToList());
                    byGps.Sort((a, b) => CompareWithReputation(a, b, scores));
                }
                else
                {
                    byGps.Sort((a, b) => CompareDouble(a.DistanceKm, b.DistanceKm));
                }
            }

            // Pack prioritaire livreur (option payante) : la priorité ACTIVE passe en tête,
            // plafonnée par vague pour ne pas assécher les non-abonnés. Elle ne modifie ni le
            // rayon, ni la disponibilité, ni les exclusions — l'attribution reste à l'acceptation.
            byGps = ApplyPriorityOrdering(byGps, nowUtc);

            return byGps.Take(count).ToList();
        }

        /// <summary>
        /// Note moyenne par livreur (seulement à partir de <see cref="RiderReputationOptions.MinimumRatingsBeforeFiltering"/>
        /// avis — sinon <c>null</c> : le livreur est « neutre » pour la pondération). Les livreurs
        /// sans candidature ne figurent pas dans la map (accès = null).
        /// </summary>
        private async Task<IReadOnlyDictionary<Guid, double?>> LoadAverageScoresAsync(
            IReadOnlyCollection<Guid> riderIds)
        {
            var scores = riderIds.ToDictionary(id => id, id => null as double?);
            if (riderIds.Count == 0)
                return scores;

            var rows = await _context.RiderRatings.AsNoTracking()
                .GroupBy(r => r.RiderUserId)
                .Where(g => g.Count() >= _reputation.MinimumRatingsBeforeFiltering)
                .Select(g => new { RiderId = g.Key, Average = g.Average(r => r.Score) })
                .ToListAsync();

            foreach (var row in rows)
                if (scores.TryGetValue(row.RiderId, out _))
                    scores[row.RiderId] = row.Average;

            return scores;
        }

        /// <summary>
        /// Comparateur du matching pondéré : réputation décroissante (les « neutres », sans assez
        /// d'avis, passent après les notés), puis distance croissante (déterministe).
        /// </summary>
        public static int CompareWithReputation(
            NearestRiderDto a, NearestRiderDto b, IReadOnlyDictionary<Guid, double?> scores)
        {
            var scoreA = scores.TryGetValue(a.RiderUserId, out var rawA) ? rawA : null;
            var scoreB = scores.TryGetValue(b.RiderUserId, out var rawB) ? rawB : null;

            if (scoreA is null && scoreB is null)
                return CompareDouble(a.DistanceKm, b.DistanceKm);
            if (scoreA is null)
                return 1;
            if (scoreB is null)
                return -1;

            var byScore = CompareDouble(scoreB.Value, scoreA.Value);
            return byScore != 0 ? byScore : CompareDouble(a.DistanceKm, b.DistanceKm);
        }

        /// <summary>Comparaison numérique réutilisable (le langage n'a pas de CompareTo sur double).</summary>
        private static int CompareDouble(double a, double b)
            => a < b ? -1 : (a > b ? 1 : 0);

        /// <summary>
        /// Remonte en tête les livreurs dont la priorité (« pack prioritaire ») est active, dans
        /// l'ordre déjà établi par le tri de base (distance ou réputation). Le nombre de places
        /// réservées est plafonné par <see cref="RiderPriorityOptions.MaxPriorityRidersPerWave"/>
        /// (équité) : au-delà, les prioritaires gardent leur rang géographique.
        /// Cette réorganisation n'ajoute ni ne retire aucun candidat.
        /// </summary>
        public List<NearestRiderDto> ApplyPriorityOrdering(List<NearestRiderDto> ordered, DateTime utcNow)
            => ApplyPriorityOrdering(ordered, utcNow, _priority.MaxPriorityRidersPerWave);

        /// <summary>
        /// Variante statique du tri prioritaire (testable isolément, comme
        /// <see cref="CompareWithReputation"/>) : <paramref name="maxPriorityRidersPerWave"/> place
        /// le plafond d'équité. L'ordre relatif des non-prioritaires est préservé.
        /// </summary>
        public static List<NearestRiderDto> ApplyPriorityOrdering(
            List<NearestRiderDto> ordered, DateTime utcNow, int maxPriorityRidersPerWave)
        {
            var cap = maxPriorityRidersPerWave;
            if (cap <= 0)
                return ordered;

            var boosted = new List<NearestRiderDto>(cap);
            var rest = new List<NearestRiderDto>(ordered.Count);

            foreach (var rider in ordered)
            {
                if (boosted.Count < cap && IsPriorityActive(rider, utcNow))
                    boosted.Add(rider);
                else
                    rest.Add(rider);
            }

            if (boosted.Count == 0)
                return ordered;

            boosted.AddRange(rest);
            return boosted;
        }

        /// <summary>Priorité de proposition active pour ce candidat (pack livreur non expiré).</summary>
        public static bool IsPriorityActive(NearestRiderDto rider, DateTime utcNow)
            => rider.PriorityUntilUtc is { } until && until > utcNow;
    }
}
