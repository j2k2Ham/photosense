// StoreKit 2 for Trovolve's C# code, which cannot call Swift itself. Each function starts the work and answers once,
// through the callback it is given, with JSON: {"ok":true,...} or {"ok":false,"error":"what to tell the person"}.
// What a purchase means as a plan is worked out in shared code (PlanProducts); this only asks the App Store.

import Foundation
import StoreKit
import UIKit

/// How an answer is handed back: the number the caller gave, and the JSON, as UTF-8.
public typealias TrovolveAnswer = @convention(c) (Int, UnsafePointer<CChar>?) -> Void

private func answer(_ done: TrovolveAnswer, _ id: Int, _ value: [String: Any]) {
    let data = (try? JSONSerialization.data(withJSONObject: value)) ?? Data(#"{"ok":false,"error":"The App Store's answer could not be read. Try again."}"#.utf8)
    String(decoding: data, as: UTF8.self).withCString { done(id, $0) }
}

// What the person is told: the App Store out of reach, or anything else it could not do.
private func failure(_ error: Error) -> [String: Any] {
    if case StoreKitError.networkError(_) = error { return ["ok": false, "error": "The App Store could not be reached. Try again."] }
    return ["ok": false, "error": "The App Store could not do that. Try again."]
}

/// Finishes every transaction that arrives while the app runs: renewals, and purchases a parent approved later.
@_cdecl("trovolve_store_start")
public func trovolve_store_start() {
    Task.detached {
        for await update in Transaction.updates {
            if case .verified(let transaction) = update { await transaction.finish() }
        }
    }
}

/// What the App Store sells of the ids given, one to a line, with its prices for the person's country.
@_cdecl("trovolve_store_products")
public func trovolve_store_products(_ ids: UnsafePointer<CChar>, _ id: Int, _ done: TrovolveAnswer) {
    let wanted = String(cString: ids).split(separator: "\n").map(String.init)
    Task {
        do {
            let products = try await Product.products(for: wanted)
            answer(done, id, ["ok": true, "products": products.map { product -> [String: Any] in
                ["id": product.id, "price": product.displayPrice, "amount": NSDecimalNumber(decimal: product.price).stringValue, "currency": product.priceFormatStyle.currencyCode]
            }])
        } catch {
            answer(done, id, failure(error))
        }
    }
}

/// What the person has bought and still has, checked by the App Store's own signature.
@_cdecl("trovolve_store_purchases")
public func trovolve_store_purchases(_ id: Int, _ done: TrovolveAnswer) {
    Task {
        var bought: [[String: Any]] = []
        for await result in Transaction.currentEntitlements {
            guard case .verified(let transaction) = result, transaction.revocationDate == nil else { continue }
            var item: [String: Any] = ["id": transaction.productID, "bought": transaction.purchaseDate.timeIntervalSince1970, "trial": transaction.offerType == .introductory]
            if let expires = transaction.expirationDate { item["expires"] = expires.timeIntervalSince1970 }
            bought.append(item)
        }
        answer(done, id, ["ok": true, "purchases": bought])
    }
}

/// Puts the App Store's own sheet for buying a product in front of the person, and answers once it has gone.
@_cdecl("trovolve_store_buy")
public func trovolve_store_buy(_ productId: UnsafePointer<CChar>, _ id: Int, _ done: TrovolveAnswer) {
    let wanted = String(cString: productId)
    Task {
        do {
            guard let product = try await Product.products(for: [wanted]).first else {
                answer(done, id, ["ok": false, "error": "The App Store does not sell that plan here."])
                return
            }
            switch try await product.purchase() {
            case .success(.verified(let transaction)):
                await transaction.finish()
                answer(done, id, ["ok": true, "outcome": "bought"])
            case .success(.unverified(_, _)):
                answer(done, id, ["ok": false, "error": "The App Store could not confirm the purchase. Try again."])
            case .pending:
                answer(done, id, ["ok": true, "outcome": "pending"])
            default:
                answer(done, id, ["ok": true, "outcome": "cancelled"])
            }
        } catch {
            answer(done, id, failure(error))
        }
    }
}

/// Asks the App Store again for everything bought with the same Apple Account.
@_cdecl("trovolve_store_restore")
public func trovolve_store_restore(_ id: Int, _ done: TrovolveAnswer) {
    Task {
        do {
            try await AppStore.sync()
            answer(done, id, ["ok": true])
        } catch {
            answer(done, id, failure(error))
        }
    }
}

/// Opens the App Store's own page for changing or cancelling the subscription, over the app.
@_cdecl("trovolve_store_manage")
public func trovolve_store_manage(_ id: Int, _ done: TrovolveAnswer) {
    Task { @MainActor in
        guard let scene = UIApplication.shared.connectedScenes.compactMap({ $0 as? UIWindowScene }).first else {
            answer(done, id, ["ok": false, "error": "Open Trovolve to manage the subscription."])
            return
        }
        do {
            try await AppStore.showManageSubscriptions(in: scene)
            answer(done, id, ["ok": true])
        } catch {
            answer(done, id, failure(error))
        }
    }
}
