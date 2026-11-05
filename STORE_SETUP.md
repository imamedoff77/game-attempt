# In-app purchases: setup (Faza 6)

How the store works, and what must be done in the Google Play Console and on the server before real money can flow. Code is finished and tested with a fake payment path; the steps below need the developer's Google account (they cannot be done from this repository).

## How a purchase flows

1. The player taps a price in the Store page (`DemoHud.Store.cs`). `Billing.cs` → `Plugins/Android/BillingBridge.java` opens Google Play's payment sheet (Play Billing Library 7.1.1, added in `Plugins/Android/mainTemplate.gradle`).
2. Google reports a paid purchase with a **purchase token**. The game sends `productId + token` to the account server (`POST /v1/store/purchase`, signed-in players only).
3. The server asks the Google Play Developer API (`purchases.products.get`) whether the token is real, paid and for that product (`GooglePlayVerifier`), records it **once** (a token can never be used twice, not even by another account), and lists the credits as *pending* (or starts the booster).
4. Only then the game **consumes** the purchase in Play (this also acknowledges it; Google refunds unacknowledged purchases after 3 days) and adds the pending credits to the wallet (`AddPurchased`), then tells the server (`POST /v1/store/ack`).
5. If the phone or the server dies between any two steps, nothing is lost: an unconsumed purchase is found again by `queryPurchasesAsync` at the next start and re-sent (the server answers "already processed"); pending credits stay on the server until acknowledged, and the phone remembers which grant ids it already added.

Wallet: credits are three never-decreasing counters (earned, spent, bought; `CampaignProgress`). Merging two phones takes the maximum of each, so a purchase made on one phone shows up on the other without ever being counted twice.

Boosters: `booster_credits_x2_7d` doubles campaign mission payouts for 7 days (`DemoGame.RecordMission`). Buying it again while active extends it (server side, `AccountStore`).

## Products to create in the Play Console

Monetize → Products → **In-app products** (not subscriptions). All are consumable in the game's logic (the game consumes them after delivery). Ids must match exactly:

| Product id | Gives | Suggested price (change freely) |
|---|---|---|
| `credits_500` | 500 credits | 0.99 |
| `credits_1500` | 1500 credits | 2.49 |
| `credits_4000` | 4000 credits | 4.99 |
| `booster_credits_x2_7d` | double mission credits for 7 days | 1.99 |

The catalog lives in `server/AccountServer/Store.cs` (`Catalog.All`) and `Billing.ProductIds` in the game. Prices are never in code; the game shows Google's localized price.

## Server setup

1. Play Console → Setup → **API access**: link a Google Cloud project, create a **service account**, grant it *View financial data* and *Manage orders and subscriptions* permission for the app, download its JSON key.
2. Put the key file on the account server (outside the repository) and set:
   - `Account__Store__PackageName=com.imamedoff.irondoctrine` (the final package name once chosen)
   - `Account__Store__ServiceAccountJsonPath=/path/to/key.json`
3. Without these two settings the store is closed (`503 store_unavailable`) and the game shows "The store is not available right now."
4. **Never** set `Account__Store__AllowDevPurchases=true` on a public server: it makes any token starting with `dev-` count as paid. It exists for local development with development builds of the game (the game builds with *Development Build* use a fake purchase when Google returns no products).

## Testing with Google

- Upload a build to an *internal testing* track (the app must exist in the Play Console), add your Google account as a **license tester** (Setup → License testing), install from Play, buy: test purchases are free and go through the same verification.
- After a test purchase check `select * from purchases` in the account database: one row, `delivered = true` once the phone collected it.

## Known limits (be honest before launch)

- Credits earned in play, daily rewards, Armory levels and the double-credits clock still come from the phone (profile sync merges by max, the device clock decides daily rewards and booster expiry). A cheater can edit their own offline credits; they cannot touch other players, and ranked matches use no Armory, so it only affects their own single-player progress. Making credits fully server-authoritative (server-side wallet, server clock) is Faza 5.5c / a later step. **Purchased credits are safe from loss, but not from copying**: do not sell anything that gives a competitive advantage in ranked matches.
- Refunds: if Google refunds a purchase the credits stay. Handling refunds (voided purchases API) is optional later work.
- Restore on a new phone: the server already knows the purchases; signing in collects any pending grant.
