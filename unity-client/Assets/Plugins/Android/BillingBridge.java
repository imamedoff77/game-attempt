package game.store;

import android.app.Activity;
import android.content.Context;
import com.android.billingclient.api.AcknowledgePurchaseParams;
import com.android.billingclient.api.BillingClient;
import com.android.billingclient.api.BillingClientStateListener;
import com.android.billingclient.api.BillingFlowParams;
import com.android.billingclient.api.BillingResult;
import com.android.billingclient.api.ConsumeParams;
import com.android.billingclient.api.PendingPurchasesParams;
import com.android.billingclient.api.ProductDetails;
import com.android.billingclient.api.Purchase;
import com.android.billingclient.api.PurchasesUpdatedListener;
import com.android.billingclient.api.QueryProductDetailsParams;
import com.android.billingclient.api.QueryPurchasesParams;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Google Play Billing for the game (C#: Billing.cs). Everything the game sells is a consumable in-app product: the game gives the
 * purchase token to the account server, which checks it with Google, and only then consumes the purchase here (which also acknowledges it).
 * Events go to the listener on a background thread as (type, a, b):
 *   ready ""            connected to Play
 *   products "id|price;id|price"    the products that exist, with their localized price
 *   purchase "productId" "token"    a paid purchase (from buy or found by queryOwned)
 *   owned "productId|token;..."     purchases not consumed yet (paid, not finished)
 *   consumed "token"
 *   cancelled / pending / error "message"
 */
public final class BillingBridge {
    public interface Listener {
        void onEvent(String type, String a, String b);
    }

    private static BillingClient client;
    private static Listener listener;
    private static final Map<String, ProductDetails> details = new HashMap<>();

    private BillingBridge() {}

    private static void send(String type, String a, String b) {
        Listener l = listener;
        if (l != null) l.onEvent(type, a == null ? "" : a, b == null ? "" : b);
    }

    public static void init(Context context, Listener events) {
        listener = events;
        if (client != null && client.isReady()) { send("ready", "", ""); return; }
        try {
            client = BillingClient.newBuilder(context.getApplicationContext())
                    .setListener(new PurchasesUpdatedListener() {
                        @Override
                        public void onPurchasesUpdated(BillingResult result, List<Purchase> purchases) {
                            int code = result.getResponseCode();
                            if (code == BillingClient.BillingResponseCode.OK && purchases != null) {
                                for (Purchase p : purchases) report(p);
                            } else if (code == BillingClient.BillingResponseCode.USER_CANCELED) {
                                send("cancelled", "", "");
                            } else {
                                send("error", "purchase " + code, "");
                            }
                        }
                    })
                    .enablePendingPurchases(PendingPurchasesParams.newBuilder().enableOneTimeProducts().build())
                    .build();
            client.startConnection(new BillingClientStateListener() {
                @Override
                public void onBillingSetupFinished(BillingResult result) {
                    if (result.getResponseCode() == BillingClient.BillingResponseCode.OK) send("ready", "", "");
                    else send("error", "setup " + result.getResponseCode(), "");
                }

                @Override
                public void onBillingServiceDisconnected() {
                    // the next call reconnects through init
                }
            });
        } catch (Throwable t) {
            send("error", "init " + t.getMessage(), "");
        }
    }

    private static void report(Purchase p) {
        if (p.getPurchaseState() == Purchase.PurchaseState.PURCHASED) {
            for (String product : p.getProducts()) send("purchase", product, p.getPurchaseToken());
        } else if (p.getPurchaseState() == Purchase.PurchaseState.PENDING) {
            send("pending", "", "");
        }
    }

    public static void queryProducts(String[] ids) {
        if (client == null || !client.isReady()) { send("error", "not ready", ""); return; }
        List<QueryProductDetailsParams.Product> list = new ArrayList<>();
        for (String id : ids)
            list.add(QueryProductDetailsParams.Product.newBuilder().setProductId(id).setProductType(BillingClient.ProductType.INAPP).build());
        client.queryProductDetailsAsync(QueryProductDetailsParams.newBuilder().setProductList(list).build(), (result, found) -> {
            if (result.getResponseCode() != BillingClient.BillingResponseCode.OK) { send("error", "products " + result.getResponseCode(), ""); return; }
            StringBuilder sb = new StringBuilder();
            for (ProductDetails d : found) {
                ProductDetails.OneTimePurchaseOfferDetails offer = d.getOneTimePurchaseOfferDetails();
                if (offer == null) continue;
                details.put(d.getProductId(), d);
                if (sb.length() > 0) sb.append(';');
                sb.append(d.getProductId()).append('|').append(offer.getFormattedPrice().replace(';', ',').replace('|', '/'));
            }
            send("products", sb.toString(), "");
        });
    }

    public static void buy(final Activity activity, String productId) {
        final ProductDetails d = details.get(productId);
        if (client == null || !client.isReady() || d == null) { send("error", "unavailable", ""); return; }
        activity.runOnUiThread(() -> {
            List<BillingFlowParams.ProductDetailsParams> params = new ArrayList<>();
            params.add(BillingFlowParams.ProductDetailsParams.newBuilder().setProductDetails(d).build());
            BillingResult result = client.launchBillingFlow(activity, BillingFlowParams.newBuilder().setProductDetailsParamsList(params).build());
            if (result.getResponseCode() != BillingClient.BillingResponseCode.OK) send("error", "flow " + result.getResponseCode(), "");
        });
    }

    /** Purchases that were paid but never finished (the game was closed, the account server was unreachable). */
    public static void queryOwned() {
        if (client == null || !client.isReady()) { send("error", "not ready", ""); return; }
        client.queryPurchasesAsync(QueryPurchasesParams.newBuilder().setProductType(BillingClient.ProductType.INAPP).build(), (result, purchases) -> {
            if (result.getResponseCode() != BillingClient.BillingResponseCode.OK) { send("error", "owned " + result.getResponseCode(), ""); return; }
            StringBuilder sb = new StringBuilder();
            for (Purchase p : purchases) {
                if (p.getPurchaseState() != Purchase.PurchaseState.PURCHASED) continue;
                for (String product : p.getProducts()) {
                    if (sb.length() > 0) sb.append(';');
                    sb.append(product).append('|').append(p.getPurchaseToken());
                }
            }
            send("owned", sb.toString(), "");
        });
    }

    /** Finishes a purchase the account server accepted, so the same product can be bought again. */
    public static void consume(final String token) {
        if (client == null || !client.isReady()) { send("error", "not ready", ""); return; }
        client.consumeAsync(ConsumeParams.newBuilder().setPurchaseToken(token).build(), (result, t) -> {
            if (result.getResponseCode() == BillingClient.BillingResponseCode.OK) send("consumed", token, "");
            else send("error", "consume " + result.getResponseCode(), "");
        });
    }
}
