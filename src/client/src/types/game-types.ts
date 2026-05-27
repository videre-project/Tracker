/**
 * TypeScript interfaces for MTGOSDK game action JSON shapes.
 *
 * These types describe the output of GameAction.ToJSON() which uses MTGOSDK's
 * custom serialization pipeline (PropertyFilter + IJsonSerializable). They
 * cannot be auto-generated from swagger because the serialization is runtime-
 * driven and not schema-driven like the rest of our Tracker API models.
 *
 * Key conventions:
 *   - All properties are camelCase (from JsonNamingPolicy.CamelCase)
 *   - $type is the action class name (discriminator)
 *   - Properties whose C# type has [NonSerializable] serialize as strings
 *     via ToString() (e.g., GameCard → "Name (ID: x, SourceId: y)")
 *   - Null properties are omitted (JsonIgnoreCondition.WhenWritingNull)
 */

//
// Shared sub-types
//

/** TargetSet — serialized from MTGOSDK.API.Play.Games.Types.TargetSet */
export interface TargetSet {
  reminderText?: string
  description?: string
  minimumTargets?: number
  maximumTargets?: number
  isSet?: boolean
  /** Each entry is Targetable.ToString() — either a card or player string */
  currentTargets?: string[]
  targetRequirements?: unknown
}

/** Distribution — from CombatDamageAssignmentAction.Distributions */
export interface Distribution {
  target?: string
  amount?: number
  minimum?: number
  maximum?: number
}

/** CardSelectorChoice — from CardSelectorAction.Choices */
export interface CardSelectorChoice {
  name?: string
  id?: number
}

/** NamedValue — from SelectFromListAction items */
export interface NamedValue {
  name?: string
  value?: number
}

//
// Base action (common to all 15 subclasses)
//

export interface BaseAction {
  /** Discriminator — action class name (e.g., "CardAction", "PrimitiveAction") */
  $type: string
  /** Human-readable action name (e.g., "Cast", "Activate", "Cancel") */
  name?: string
  /** Server-assigned action identifier */
  actionId?: number
  /** ActionType enum value as string */
  type?: string
  /** Optional response payload included by some prompt-driven actions. */
  response?: string | number | boolean | null
}

//
// Action subclasses
//

/** CardAction — casting, activating, or using a card ability */
export interface CardAction extends BaseAction {
  $type: "CardAction"
  /** GameCard.ToString() = "Name (ID: x, SourceId: y)" */
  card?: string
  targets?: TargetSet[]
  requiresTargets?: boolean
  isTargetsSet?: boolean
  isManaAbility?: boolean
}

/** DistributingCardAction — distributing damage/counters among targets */
export interface DistributingCardAction extends BaseAction {
  $type: "DistributingCardAction"
  card?: string
  targets?: TargetSet[]
  requiresTargets?: boolean
  isTargetsSet?: boolean
  isManaAbility?: boolean
  areTargetsEditable?: boolean
  minimumTotal?: number
  maximumTotal?: number
}

/** SelectFromListAction — choosing from a list of named items */
export interface SelectFromListAction extends BaseAction {
  $type: "SelectFromListAction"
  itemType?: string
  availableItems?: NamedValue[]
  selectedItem?: NamedValue
}

/** NumericAction — choosing a number within a range */
export interface NumericAction extends BaseAction {
  $type: "NumericAction"
  chosenNumber?: number
  minimum?: number
  maximum?: number
  initial?: number
}

/** OrderingAction — ordering a set of targets */
export interface OrderingAction extends BaseAction {
  $type: "OrderingAction"
  /** GameCard.ToString() */
  source?: string
  /** Each entry is GameCard.ToString() */
  orderedTargets?: string[]
}

/** SelectPlayerAction — selecting a player */
export interface SelectPlayerAction extends BaseAction {
  $type: "SelectPlayerAction"
  /** Each entry is GamePlayer.ToString() (player name) */
  availablePlayers?: string[]
  /** GamePlayer.ToString() */
  selectedPlayer?: string
}

/** CombatDamageAssignmentAction — assigning combat damage */
export interface CombatDamageAssignmentAction extends BaseAction {
  $type: "CombatDamageAssignmentAction"
  /** GameCard.ToString() */
  source?: string
  distributions?: Distribution[]
  minimumTotal?: number
  maximumTotal?: number
}

/** CardSelectorAction — selecting a card from choices */
export interface CardSelectorAction extends BaseAction {
  $type: "CardSelectorAction"
  choices?: CardSelectorChoice[]
  selectedCard?: number
}

/** CardWishAction — wishing for a card from outside the game */
export interface CardWishAction extends BaseAction {
  $type: "CardWishAction"
  wishedCard?: unknown
}

/** FunctionKeyMessageAction — pressing a function key */
export interface FunctionKeyMessageAction extends BaseAction {
  $type: "FunctionKeyMessageAction"
  key?: string
}

/** ToggleMessageAction — toggling a function key state */
export interface ToggleMessageAction extends BaseAction {
  $type: "ToggleMessageAction"
  key?: string
  toggleState?: boolean
}

/** PrimitiveAction — simple yes/no/ok actions */
export interface PrimitiveAction extends BaseAction {
  $type: "PrimitiveAction"
}

/** ConcedeGameAction — conceding the game */
export interface ConcedeGameAction extends BaseAction {
  $type: "ConcedeGameAction"
}

/** LocalAction — client-side action */
export interface LocalAction extends BaseAction {
  $type: "LocalAction"
}

/** UndoAction — undoing a previous action */
export interface UndoAction extends BaseAction {
  $type: "UndoAction"
}

//
// Discriminated union
//

/** Any finalized game action. Discriminate on $type. */
export type GameAction =
  | CardAction
  | DistributingCardAction
  | SelectFromListAction
  | NumericAction
  | OrderingAction
  | SelectPlayerAction
  | CombatDamageAssignmentAction
  | CardSelectorAction
  | CardWishAction
  | FunctionKeyMessageAction
  | ToggleMessageAction
  | PrimitiveAction
  | ConcedeGameAction
  | LocalAction
  | UndoAction

//
// Helpers
//

/** Extract the card name from a GameCard.ToString() string like "Name (ID: x, SourceId: y)" */
export function parseCardName(cardStr: string | null | undefined): string | null {
  if (!cardStr) return null
  const paren = cardStr.indexOf(" (ID:")
  return paren > 0 ? cardStr.slice(0, paren) : cardStr
}

/** Type guard: action has a card property (CardAction or DistributingCardAction) */
export function isCardAction(action: GameAction): action is CardAction | DistributingCardAction {
  return "card" in action && action.card != null
}
