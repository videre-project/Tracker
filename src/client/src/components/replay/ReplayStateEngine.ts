import type {
  ReplayData,
  ReplaySnapshot,
  BoardState,
  PlayerState,
  CardState,
  ZoneTransfer,
  CardChange,
  PlayerChange,
} from "@/types/replay-types"

/**
 * Reconstructs board state from replay data by applying snapshot deltas
 * forward or backward. Maintains a cursor into the snapshot array.
 */
export class ReplayStateEngine {
  private data: ReplayData
  private _currentIndex: number = -1

  // Current reconstructed state
  private players = new Map<number, PlayerState>()
  private cards = new Map<number, CardState>()
  private zones = new Map<string, CardState[]>()

  /** Maps each cardId to its root ancestor cardId (stable across zone changes). */
  private lineageMap = new Map<number, number>()
  /** Maps each cardId to its direct predecessor cardId (for retiring/restoring). */
  private parentMap = new Map<number, number>()
  /** Tracks ability supersessions: newCardId → retiredCardId, for backward stepping. */
  private supersededAbilities = new Map<number, number>()

  constructor(data: ReplayData) {
    this.data = data
    this.buildLineageMap()
  }

  /** Follow each card's sourceId chain to find the root ancestor. */
  private buildLineageMap(): void {
    // Collect ancestry edges from ReplayCard data (initial/fallback links).
    // Abilities are excluded — their sourceId references the source permanent,
    // not a zone-chain predecessor.
    const sourceOf = new Map(this.data.cards.map(c => [
      c.cardId,
      (c.isTriggeredAbility || c.isActivatedAbility
        || c.sourceId == null || c.sourceId <= 0) ? null : c.sourceId,
    ]))

    // Override with zone transfer data (authoritative), processed in
    // chronological order. Track consumed sourceIds to detect reuse after
    // library shuffles — MTGO can reassign a ThingID to a different physical
    // card after shuffling, so the first card to claim a sourceId wins.
    //
    // Abilities are excluded — their sourceId references the source permanent
    // (not a zone-chain predecessor), which would create false lineage links
    // and steal the real card's ancestry chain.
    const abilityCardIds = new Set(
      this.data.cards
        .filter(c => c.isTriggeredAbility || c.isActivatedAbility)
        .map(c => c.cardId)
    )
    const consumedSourceIds = new Set<number>()
    for (const snap of this.data.snapshots) {
      for (const zt of snap.zoneTransfers) {
        if (zt.sourceId != null && zt.sourceId !== zt.cardId
            && !abilityCardIds.has(zt.cardId)) {
          if (consumedSourceIds.has(zt.sourceId)) {
            // sourceId already consumed by an earlier zone transfer —
            // this is ID reuse after a shuffle, not a real lineage link
            sourceOf.set(zt.cardId, null)
          } else {
            sourceOf.set(zt.cardId, zt.sourceId)
            consumedSourceIds.add(zt.sourceId)
          }
        }
      }
    }

    // Store the direct parent map for forward/backward stepping
    for (const [id, src] of sourceOf) {
      if (src != null) this.parentMap.set(id, src)
    }

    // Resolve each card to its root ancestor
    for (const card of this.data.cards) {
      let current = card.cardId
      const visited = new Set<number>()
      while (true) {
        if (visited.has(current)) break
        visited.add(current)
        const src = sourceOf.get(current)
        if (src == null) break
        current = src
      }
      this.lineageMap.set(card.cardId, current)
    }

  }

  get currentIndex(): number {
    return this._currentIndex
  }

  get snapshotCount(): number {
    return this.data.snapshots.length
  }

  get currentSnapshot(): ReplaySnapshot | null {
    if (this._currentIndex < 0 || this._currentIndex >= this.data.snapshots.length)
      return null
    return this.data.snapshots[this._currentIndex]
  }

  /** Initialize to the state before any snapshot has been applied. */
  reset(): BoardState {
    this.players.clear()
    this.cards.clear()
    this.zones.clear()
    this._currentIndex = -1
    return this.buildBoardState()
  }

  /** Step forward to snapshot at targetIndex. */
  stepTo(targetIndex: number): BoardState {
    if (targetIndex < -1) targetIndex = -1
    if (targetIndex >= this.data.snapshots.length)
      targetIndex = this.data.snapshots.length - 1

    if (targetIndex > this._currentIndex) {
      // Step forward
      for (let i = this._currentIndex + 1; i <= targetIndex; i++) {
        this.applyForward(i)
      }
    } else if (targetIndex < this._currentIndex) {
      // Step backward
      for (let i = this._currentIndex; i > targetIndex; i--) {
        this.applyBackward(i)
      }
    }

    return this.buildBoardState()
  }

  /** Step forward by one snapshot. */
  stepForward(): BoardState {
    return this.stepTo(this._currentIndex + 1)
  }

  /** Step backward by one snapshot. */
  stepBackward(): BoardState {
    return this.stepTo(this._currentIndex - 1)
  }

  /** Jump to a specific snapshot index. */
  jumpTo(index: number): BoardState {
    // For large jumps, rebuild from scratch for correctness
    if (index < this._currentIndex - 5) {
      this.reset()
    }
    return this.stepTo(index)
  }

  private applyForward(snapshotIndex: number): void {
    const snapshot = this.data.snapshots[snapshotIndex]
    this._currentIndex = snapshotIndex

    // Add cards first seen at this snapshot
    for (const card of this.data.cards) {
      if (card.firstSeenSnapshotIndex === snapshotIndex) {
        const state: CardState = {
          cardId: card.cardId,
          lineageId: this.lineageMap.get(card.cardId) ?? card.cardId,
          name: card.name,
          rulesText: card.rulesText,
          manaCost: card.manaCost,
          catalogId: card.catalogId,
          zone: card.initialZone,
          power: card.initialPower,
          toughness: card.initialToughness,
          initialPower: card.initialPower,
          initialToughness: card.initialToughness,
          isTapped: card.isTapped,
          isAttacking: false,
          isBlocking: false,
          attackingOrderIds: [],
          blockingOrderIds: [],
          counters: {},
          abilities: [],
          initialAbilities: [],
          blueText: null,
          typeLine: null,
          controllerId: card.ownerId,
          ownerId: card.ownerId,
          isToken: card.isToken,
          isLand: card.isLand,
          isActivatedAbility: card.isActivatedAbility,
          isTriggeredAbility: card.isTriggeredAbility,
          associations: {},
          attachedToId: 0,
          isExiledOnBattlefield: false,
        }
        this.cards.set(card.cardId, state)
        this.addToZone(state.zone, state)
      }
    }

    // Add players first seen at this snapshot
    for (const player of this.data.players) {
      if (!this.players.has(player.playerIndex)) {
        this.players.set(player.playerIndex, {
          playerIndex: player.playerIndex,
          name: player.name,
          life: player.initialLife,
          handCount: player.initialHandCount,
          libraryCount: player.initialLibraryCount,
          graveyardCount: player.initialGraveyardCount,
          manaPool: player.initialManaPool,
          isActivePlayer: player.isActivePlayer,
          hasPriority: false,
          clockRemaining: player.clockRemaining ?? null,
          counters: {},
          avatarId: player.avatarId ?? 0,
        })
      }
    }

    // Apply zone transfers
    for (const zt of snapshot.zoneTransfers) {
      this.applyZoneTransfer(zt)
    }

    // Apply card changes
    for (const cc of snapshot.cardChanges) {
      this.applyCardChange(cc, true)
    }

    // Apply player changes
    for (const pc of snapshot.playerChanges) {
      this.applyPlayerChange(pc, true)
    }
  }

  private applyBackward(snapshotIndex: number): void {
    const snapshot = this.data.snapshots[snapshotIndex]

    // Reverse player changes
    for (const pc of [...snapshot.playerChanges].reverse()) {
      this.applyPlayerChange(pc, false)
    }

    // Reverse card changes
    for (const cc of [...snapshot.cardChanges].reverse()) {
      this.applyCardChange(cc, false)
    }

    // Reverse zone transfers
    for (const zt of [...snapshot.zoneTransfers].reverse()) {
      this.reverseZoneTransfer(zt)
    }

    // Remove cards first seen at this snapshot and restore their predecessors
    for (const card of this.data.cards) {
      if (card.firstSeenSnapshotIndex === snapshotIndex) {
        const state = this.cards.get(card.cardId)
        if (state) {
          this.removeFromZone(state.zone, card.cardId)
          this.cards.delete(card.cardId)
        }
      }
    }

    this._currentIndex = snapshotIndex - 1
  }

  /**
   * Apply a zone transfer forward.
   *
   * MTGO gives cards a new ThingID when they change zones ("Moved").
   * The new card is already placed at initialZone during applyForward's
   * card initialization — zone transfers only need to:
   *   - Moved:    retire the OLD card (sourceId) from its zone
   *   - Arrived:  no-op (card already placed by initialization)
   *   - Departed: remove card from its zone
   */
  private applyZoneTransfer(zt: ZoneTransfer): void {
    if (zt.type === "Moved" && zt.sourceId != null) {
      // Remove the old card (sourceId) from its prior zone.
      const oldCard = this.cards.get(zt.sourceId)
      if (oldCard) {
        this.removeFromZone(oldCard.zone, zt.sourceId)
      }
      // For new-ThingID moves, the new card was already initialized at toZone.
      // For same-ThingID moves (shared zone → shared zone, e.g. Stack →
      // Battlefield), the card wasn't re-initialized — move it to the
      // destination zone.
      const newCard = this.cards.get(zt.cardId)
      if (newCard && zt.toZone && newCard.zone !== zt.toZone) {
        newCard.zone = zt.toZone
        this.addToZone(zt.toZone, newCard)
      }
      // When a new ability arrives on the Stack, MTGO may create a fresh
      // ThingID that supersedes a prior ability with the same sourceId
      // (e.g. a triggered ability whose text updates on resolution). The
      // old ability never gets an explicit departure — remove it here.
      if (zt.toZone === "Stack" && newCard) {
        this.retireSupersededAbility(newCard)
      }
      return
    }

    if (zt.type === "Arrived") {
      // Card was placed at initialZone during card initialization.
      // If it has a sourceId, retire the old ThingID it supersedes — but only
      // if buildLineageMap confirmed this is a real lineage link (not a false
      // one from sourceId reuse after a library shuffle).
      if (zt.sourceId != null && zt.sourceId !== zt.cardId) {
        if (this.parentMap.get(zt.cardId) === zt.sourceId) {
          const oldCard = this.cards.get(zt.sourceId)
          if (oldCard) {
            this.removeFromZone(oldCard.zone, zt.sourceId)
          }
        }
      }
      // Same ability supersession check for Arrived transfers
      const arrivedCard = this.cards.get(zt.cardId)
      if (zt.toZone === "Stack" && arrivedCard) {
        this.retireSupersededAbility(arrivedCard)
      }
      // Re-place cards that already exist but were removed from zones by a
      // prior Departed (e.g. revealed cards reusing the same ThingID across
      // multiple search effects). Only skip re-placement if the card is
      // actively present in a visible zone (Hand, Battlefield, Stack, etc.)
      // to avoid pulling in-play cards into the Revealed overlay.
      if (arrivedCard && zt.toZone && arrivedCard.zone !== zt.toZone) {
        const isActive = arrivedCard.zone !== "" && (this.zones.get(arrivedCard.zone)?.some(c => c.cardId === arrivedCard!.cardId) ?? false)
        if (!isActive) {
          arrivedCard.zone = zt.toZone
          this.addToZone(zt.toZone, arrivedCard)
        }
      }
      return
    }

    // Departed: remove from zone and clear zone tracking so a future
    // Arrived for the same cardId re-places it correctly.
    const card = this.cards.get(zt.cardId)
    if (card && zt.fromZone) {
      this.removeFromZone(zt.fromZone, zt.cardId)
      card.zone = ""
    }
  }

  /**
   * When a new ability arrives on the Stack, check for a stale ability with
   * the same sourceId that was never explicitly removed. MTGO creates a
   * fresh ThingID to "replace" the prior ability (e.g. when a triggered
   * ability's text updates on resolution), but never emits a departure for
   * the old one.
   *
   * The stack is FILO — only the top resolves at a time. So when a
   * replacement arrives, it supersedes the most recently added ability
   * from the same source (searching from the top of the stack downward).
   * Abilities arriving in the same snapshot are distinct triggers and
   * are not retired.
   */
  private retireSupersededAbility(newCard: CardState): void {
    if (!newCard.isTriggeredAbility && !newCard.isActivatedAbility) return

    const stackCards = this.zones.get("Stack")
    if (!stackCards) return

    const newCardData = this.data.cards.find(c => c.cardId === newCard.cardId)
    if (!newCardData?.sourceId) return

    // Search from the top (most recent) — FILO guarantees the topmost
    // matching ability is the one that just resolved and got replaced.
    for (let i = stackCards.length - 1; i >= 0; i--) {
      const existing = stackCards[i]
      if (existing.cardId === newCard.cardId) continue
      if (!existing.isTriggeredAbility && !existing.isActivatedAbility) continue

      const existingData = this.data.cards.find(c => c.cardId === existing.cardId)
      if (!existingData || !existingData.sourceId) continue

      // Direct sourceId match (same source permanent, no zone change).
      // For reflexive triggers (e.g. Brokers Hideout), the source permanent
      // gets a new ThingID after sacrifice, so the sourceIds differ. Fall
      // back to checking if both sourceIds resolve to the same root via
      // the card lineage chain.
      if (existingData.sourceId !== newCardData.sourceId) {
        const existingRoot = this.lineageMap.get(existingData.sourceId) ?? existingData.sourceId
        const newRoot = this.lineageMap.get(newCardData.sourceId) ?? newCardData.sourceId
        if (existingRoot !== newRoot) continue
      }

      // Only supersede if the existing ability arrived in a prior snapshot.
      // Abilities arriving in the same snapshot are distinct triggers.
      if (existingData.firstSeenSnapshotIndex >= this._currentIndex) continue

      this.removeFromZone("Stack", existing.cardId)
      this.supersededAbilities.set(newCard.cardId, existing.cardId)
      return
    }
  }

  /**
   * Reverse a supersession: re-add the old ability to the Stack.
   */
  private restoreSupersededAbility(newCardId: number): void {
    const retiredId = this.supersededAbilities.get(newCardId)
    if (retiredId == null) return

    const retiredCard = this.cards.get(retiredId)
    if (retiredCard) {
      this.addToZone("Stack", retiredCard)
    }
    this.supersededAbilities.delete(newCardId)
  }

  /**
   * Reverse a zone transfer (for backward stepping).
   */
  private reverseZoneTransfer(zt: ZoneTransfer): void {
    if (zt.type === "Moved" && zt.sourceId != null) {
      // Restore any ability that was superseded when this card arrived
      this.restoreSupersededAbility(zt.cardId)

      // For same-ThingID moves, remove from the destination zone first
      // (new-ThingID cards are cleaned up by applyBackward's card removal).
      if (zt.sourceId === zt.cardId && zt.toZone) {
        this.removeFromZone(zt.toZone, zt.cardId)
      }
      // Reverse: re-add the old card to its prior zone.
      const oldCard = this.cards.get(zt.sourceId)
      if (oldCard && zt.fromZone) {
        oldCard.zone = zt.fromZone
        this.addToZone(zt.fromZone, oldCard)
      }
      return
    }

    if (zt.type === "Arrived") {
      // Restore any ability that was superseded when this card arrived
      this.restoreSupersededAbility(zt.cardId)

      // Reverse: card will be removed during applyBackward's card cleanup.
      // If it had a confirmed sourceId link, restore the old ThingID to its zone.
      if (zt.sourceId != null && zt.sourceId !== zt.cardId) {
        if (this.parentMap.get(zt.cardId) === zt.sourceId) {
          const oldCard = this.cards.get(zt.sourceId)
          if (oldCard) {
            this.addToZone(oldCard.zone, oldCard)
          }
        }
      }
      // Reverse re-placement of reused cards: remove from destination zone
      // and reset zone to empty (matching the post-Departed state).
      const arrivedCard = this.cards.get(zt.cardId)
      if (arrivedCard && zt.toZone && arrivedCard.zone === zt.toZone) {
        const cardData = this.data.cards.find(c => c.cardId === zt.cardId)
        // Only reverse if this isn't the card's first-seen snapshot
        // (first-seen cards are cleaned up by applyBackward's card removal)
        if (cardData && cardData.firstSeenSnapshotIndex !== this._currentIndex) {
          this.removeFromZone(zt.toZone, zt.cardId)
          arrivedCard.zone = ""
        }
      }
      return
    }

    // Reverse Departed: re-add card to its zone
    const card = this.cards.get(zt.cardId)
    if (card && zt.fromZone) {
      card.zone = zt.fromZone
      this.addToZone(zt.fromZone, card)
    }
  }

  private applyCardChange(cc: CardChange, forward: boolean): void {
    const card = this.cards.get(cc.cardId)
    if (!card) return

    const value = forward ? cc.newValue : cc.oldValue

    switch (cc.property) {
      case "Power":
        card.power = value
        break
      case "Toughness":
        card.toughness = value
        break
      case "IsTapped":
        card.isTapped = value === "True"
        break
      case "IsAttacking":
        card.isAttacking = value === "True"
        break
      case "IsBlocking":
        card.isBlocking = value === "True"
        break
      case "AttackingOrders":
        try { card.attackingOrderIds = value ? JSON.parse(value) : [] }
        catch { card.attackingOrderIds = [] }
        break
      case "BlockingOrders":
        try { card.blockingOrderIds = value ? JSON.parse(value) : [] }
        catch { card.blockingOrderIds = [] }
        break
      case "Counters":
        try { card.counters = value ? JSON.parse(value) : {} }
        catch { card.counters = {} }
        break
      case "Abilities":
        try { card.abilities = value ? JSON.parse(value) : [] }
        catch { card.abilities = [] }
        // The first Abilities change establishes the card's innate abilities.
        // The oldValue of this change is the ability set before any grant/removal,
        // i.e. the innate baseline.
        if (forward && (!card.initialAbilities || card.initialAbilities.length === 0)) {
          if (cc.oldValue) {
            try { card.initialAbilities = JSON.parse(cc.oldValue) }
            catch { card.initialAbilities = [] }
          } else {
            card.initialAbilities = []
          }
        }
        break
      case "Associations": {
        try { card.associations = value ? JSON.parse(value) : {} }
        catch { card.associations = {} }
        const attachedTo = card.associations["AttachedTo"]
        card.attachedToId = attachedTo?.[0] ?? 0
        card.isExiledOnBattlefield = card.attachedToId > 0 && card.zone === "Exile"
        break
      }
      case "RulesText":
        card.rulesText = value
        break
      case "BlueText":
        card.blueText = value
        break
      case "TypeLine":
        card.typeLine = value
        break
      case "Controller":
        // Controller is stored as name; find player index
        for (const [idx, p] of this.players) {
          if (p.name === value) { card.controllerId = idx; break }
        }
        break
    }
  }

  private applyPlayerChange(pc: PlayerChange, forward: boolean): void {
    const player = this.players.get(pc.playerIndex)
    if (!player) return

    const value = forward ? pc.newValue : pc.oldValue

    switch (pc.property) {
      case "Life":
        player.life = parseInt(value ?? "0", 10)
        break
      case "HandCount":
        player.handCount = parseInt(value ?? "0", 10)
        break
      case "LibraryCount":
        player.libraryCount = parseInt(value ?? "0", 10)
        break
      case "GraveyardCount":
        player.graveyardCount = parseInt(value ?? "0", 10)
        break
      case "ManaPool":
        player.manaPool = value
        break
      case "IsActivePlayer":
        player.isActivePlayer = value === "True"
        break
      case "HasPriority":
        player.hasPriority = value === "True"
        break
      case "ClockRemaining":
        player.clockRemaining = value ? parseFloat(value) : null
        break
      case "Counters":
        try {
          player.counters = value ? JSON.parse(value) : {}
        } catch {
          player.counters = {}
        }
        break
    }
  }

  private addToZone(zone: string, card: CardState): void {
    const list = this.zones.get(zone) ?? []
    list.push(card)
    this.zones.set(zone, list)
  }

  private removeFromZone(zone: string, cardId: number): void {
    const list = this.zones.get(zone)
    if (!list) return
    const idx = list.findIndex((c) => c.cardId === cardId)
    if (idx >= 0) list.splice(idx, 1)
  }

  private cloneCard(c: CardState): CardState {
    return {
      cardId: c.cardId,
      lineageId: c.lineageId,
      name: c.name,
      rulesText: c.rulesText,
      manaCost: c.manaCost,
      catalogId: c.catalogId,
      zone: c.zone,
      power: c.power,
      toughness: c.toughness,
      initialPower: c.initialPower,
      initialToughness: c.initialToughness,
      isTapped: c.isTapped,
      isAttacking: c.isAttacking,
      isBlocking: c.isBlocking,
      attackingOrderIds: [...c.attackingOrderIds],
      blockingOrderIds: [...c.blockingOrderIds],
      counters: { ...c.counters },
      abilities: [...c.abilities],
      initialAbilities: c.initialAbilities ? [...c.initialAbilities] : [],
      blueText: c.blueText,
      typeLine: c.typeLine,
      controllerId: c.controllerId,
      ownerId: c.ownerId,
      isToken: c.isToken,
      isLand: c.isLand,
      isActivatedAbility: c.isActivatedAbility,
      isTriggeredAbility: c.isTriggeredAbility,
      associations: { ...c.associations },
      attachedToId: c.attachedToId,
      isExiledOnBattlefield: c.isExiledOnBattlefield,
    }
  }

  private clonePlayer(p: PlayerState): PlayerState {
    return {
      playerIndex: p.playerIndex,
      name: p.name,
      life: p.life,
      handCount: p.handCount,
      libraryCount: p.libraryCount,
      graveyardCount: p.graveyardCount,
      manaPool: p.manaPool,
      isActivePlayer: p.isActivePlayer,
      hasPriority: p.hasPriority,
      clockRemaining: p.clockRemaining,
      counters: { ...p.counters },
      avatarId: p.avatarId,
    }
  }

  private buildBoardState(): BoardState {
    const snapshot = this.currentSnapshot

    // Only include cards that are currently in a zone (filter out
    // retired old ThingIDs that were removed from zones by Moved transfers)
    const activeCardIds = new Set<number>()
    for (const [, list] of this.zones) {
      for (const c of list) activeCardIds.add(c.cardId)
    }

    const cards = new Map<number, CardState>()
    for (const [id, c] of this.cards) {
      if (activeCardIds.has(id)) {
        cards.set(id, this.cloneCard(c))
      }
    }

    const players = new Map<number, PlayerState>()
    for (const [id, p] of this.players) {
      players.set(id, this.clonePlayer(p))
    }

    // Zone lists reference the cloned card objects (by cardId lookup)
    const zones = new Map<string, CardState[]>()
    for (const [zone, list] of this.zones) {
      zones.set(zone, list.map(c => cards.get(c.cardId)).filter((c): c is CardState => c != null))
    }

    return {
      snapshotIndex: this._currentIndex,
      turn: snapshot?.turnNumber ?? 0,
      phase: snapshot?.currentPhase ?? "",
      players,
      zones,
      cards,
    }
  }
}
