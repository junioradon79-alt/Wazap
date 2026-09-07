namespace Wazap.Application.Exceptions;

/// <summary>
/// Envoi WhatsApp refusé par la passerelle. WhatChimp répondant <c>HTTP 200</c> même en
/// cas d'échec (corps <c>{"status":"0"}</c>), c'est cette exception — et non le code HTTP —
/// qui rend l'échec visible pour l'outbox et les alertes.
/// </summary>
public sealed class WhatsAppSendException : Exception
{
    public WhatsAppSendException(string message, bool isPermanent) : base(message)
        => IsPermanent = isPermanent;

    /// <summary>
    /// Vrai quand réessayer ne peut pas aboutir : template non approuvé, variables en
    /// nombre incorrect, ou message hors de la fenêtre de 24 h. L'outbox marque alors le
    /// message en échec immédiatement, au lieu de consommer ses tentatives pour rien.
    /// </summary>
    public bool IsPermanent { get; }
}
