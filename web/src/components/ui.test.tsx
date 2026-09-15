import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { StatusBadge, formatDateTime, formatMoney, shortId } from './ui'

describe('formatage', () => {
  it('affiche les montants en FCFA sans décimales', () => {
    // `Intl.NumberFormat('fr-FR')` sépare les milliers par une espace fine insécable (U+202F) :
    // on normalise avant de comparer.
    const normalized = formatMoney(2500).replace(/[\u202f\u00a0]/g, ' ')
    expect(normalized).toBe('2 500 FCFA')
  })

  it('affiche un tiret au lieu de « NaN FCFA » quand le montant est absent', () => {
    // `Intl.NumberFormat.format(undefined)` renvoie « NaN » : l'écran affichait
    // « NaN FCFA » sur une réponse d'API incomplète.
    expect(formatMoney(undefined)).toBe('—')
    expect(formatMoney(null)).toBe('—')
    expect(formatMoney(Number.NaN)).toBe('—')
  })

  it('n’affiche plus « Invalid Date » sur une date illisible', () => {
    // `new Date('n/a').toLocaleString()` ne LÈVE pas : le try/catch de l'ancienne version
    // était donc mort et l'écran affichait « Invalid Date ».
    expect(formatDateTime('pas-une-date')).toBe('pas-une-date')
    expect(formatDateTime(null)).toBe('—')
    expect(formatDateTime('2026-09-15T10:30:00Z')).toMatch(/2026/)
  })

  it('raccourcit un identifiant de commande', () => {
    expect(shortId('a1b2c3d4-0000-0000-0000-000000000000')).toBe('A1B2C3')
  })
})

describe('badge de statut', () => {
  it('reconnaît un libellé serveur avec espaces (« Recherche Livreur »)', () => {
    // Le libellé est envoyé par le serveur avec un espace : l'ancienne comparaison
    // (`key.includes('recherchelivreur')`) échouait et le badge restait gris.
    const { container } = render(<StatusBadge status="Recherche Livreur" />)
    expect(container.querySelector('.badge--blue')).not.toBeNull()
  })

  it('colore les états connus et retombe sur un gris neutre', () => {
    const { container: green } = render(<StatusBadge status="Livré" />)
    expect(green.querySelector('.badge--green')).not.toBeNull()

    const { container: red } = render(<StatusBadge status="Annulé" />)
    expect(red.querySelector('.badge--red')).not.toBeNull()

    // Un statut inconnu du serveur reste affiché (jamais d'écran blanc).
    render(<StatusBadge status="StatutInedit" />)
    expect(screen.getByText('StatutInedit')).toBeInTheDocument()
  })
})
