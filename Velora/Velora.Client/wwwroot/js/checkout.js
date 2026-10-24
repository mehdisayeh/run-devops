(() => {
    'use strict';
    const form = document.getElementById('checkout-form');
    if (!form) return;
    const status = document.getElementById('checkout-status');
    const error = document.getElementById('checkout-error');
    const submit = document.getElementById('place-order');
    const fields = document.getElementById('checkout-fields');
    const refresh = document.getElementById('refresh-quote');
    const cartKey = 'forme.cart.v1', pendingKey = 'velora.checkout.pending.v1';
    const money = value => new Intl.NumberFormat('en-IE', { style: 'currency', currency: 'EUR' }).format(value);
    let quote, items, pending, busy = false;
    const showError = message => { error.textContent = message; error.hidden = false; };
    function render(summary) {
        const lines = document.getElementById('checkout-lines');
        lines.replaceChildren();
        summary.items.forEach(line => {
            const row = document.createElement('div'), name = document.createElement('span'), total = document.createElement('strong');
            name.textContent = `${line.name} × ${line.quantity}`; total.textContent = money(line.lineTotal);
            row.append(name, total); lines.append(row);
        });
        document.getElementById('checkout-total').textContent = money(summary.total ?? summary.expectedTotal);
    }
    async function post(action, body) {
        const response = await fetch(`/Checkout/${action}`, {
            method: 'POST', headers: { 'Content-Type': 'application/json',
                'X-CSRF-TOKEN': form.querySelector('[name="__RequestVerificationToken"]').value },
            body: JSON.stringify(body)
        });
        let data;
        try { data = await response.json(); } catch { data = {}; }
        if (!response.ok) {
            const failure = new Error(data.detail || Object.values(data.errors || {}).flat().join(' ') || 'Checkout could not complete. Please try again.');
            failure.status = response.status;
            failure.definitive = response.headers.get('X-Checkout-Definitive') === 'true';
            if (response.status === 400 && !failure.definitive)
                failure.message = 'Checkout security validation expired. Reload this page, then retry the same order.';
            throw failure;
        }
        return data;
    }
    async function loadQuote() {
        error.hidden = true; refresh.hidden = true; submit.disabled = true;
        status.textContent = 'Checking current prices and availability…';
        try {
            const cart = JSON.parse(localStorage.getItem(cartKey) || '[]');
            if (!Array.isArray(cart) || !cart.length) throw new Error('Your bag is empty. Add products before checking out.');
            items = cart.map(i => ({ productId: i.id, quantity: i.quantity }));
            quote = await post('Quote', { items });
            render(quote); form.hidden = false; fields.disabled = false; submit.disabled = false;
            submit.textContent = 'Place order'; status.textContent = 'Review your details and order total before placing your order.';
        } catch (failure) {
            showError(failure.message); status.textContent = 'Checkout needs your attention.';
            form.hidden = false; fields.disabled = true; refresh.hidden = false;
        }
    }
    function finish(receipt) {
        if (!receipt?.id || !/^[a-f0-9]{64}$/.test(receipt.id)) throw new Error('The order response was incomplete. Retry the same submission.');
        // Persist the result before changing the bag; reloading must never submit or clear twice.
        sessionStorage.setItem(pendingKey, JSON.stringify({ receiptId: receipt.id }));
        try {
            const cart = JSON.parse(localStorage.getItem(cartKey) || '[]');
            const remaining = cart.map(item => ({ ...item, quantity: item.quantity -
                (pending.request.items.find(i => i.productId === item.id)?.quantity || 0) })).filter(i => i.quantity > 0);
            localStorage.setItem(cartKey, JSON.stringify(remaining));
        } catch { /* The receipt remains available even if bag storage fails. */ }
        location.replace(`/Checkout/Confirmation?id=${encodeURIComponent(receipt.id)}`);
    }
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || (!pending && (!quote || !form.reportValidity()))) return;
        error.hidden = true;
        if (!pending) {
            const address = Object.fromEntries(['fullName', 'email', 'line1', 'line2', 'city', 'postalCode', 'country']
                .map(name => [name, form.elements[name].value.trim()]));
            pending = { request: { items, address, expectedTotal: quote.total, quoteVersion: quote.version,
                idempotencyKey: Array.from(crypto.getRandomValues(new Uint8Array(16)), b => b.toString(16).padStart(2, '0')).join('') }, summary: quote };
            try { sessionStorage.setItem(pendingKey, JSON.stringify(pending)); }
            catch { pending = null; showError('Enable browser session storage before placing an order so retries can be handled safely.'); return; }
        }
        busy = true; submit.disabled = true; fields.disabled = true; refresh.hidden = true;
        status.textContent = 'Placing your order…';
        try { finish(await post('Place', pending.request)); }
        catch (failure) {
            showError(failure.message);
            // 5xx/network/unknown responses may have committed. Keep the exact key and payload.
            if (failure.definitive && [400, 409, 422].includes(failure.status)) {
                sessionStorage.removeItem(pendingKey); pending = null; quote = null;
                submit.disabled = true; refresh.hidden = false;
                status.textContent = 'Review updated prices and stock before submitting again.';
            } else {
                submit.disabled = false; submit.textContent = 'Retry same order';
                status.textContent = 'The outcome is not confirmed. Use Retry same order to avoid a duplicate order.';
            }
        } finally { busy = false; }
    });
    refresh.addEventListener('click', loadQuote);
    async function init() {
        try {
            sessionStorage.setItem('velora.checkout.storage-test', '1'); sessionStorage.removeItem('velora.checkout.storage-test');
            pending = JSON.parse(sessionStorage.getItem(pendingKey) || 'null');
            if (pending?.receiptId) {
                // A later checkout is a new purchase; a reload after a completed response goes to its receipt.
                location.replace(`/Checkout/Confirmation?id=${encodeURIComponent(pending.receiptId)}`); return;
            }
            if (pending?.request) {
                render(pending.summary); form.hidden = false; fields.disabled = true;
                Object.entries(pending.request.address).forEach(([key, value]) => { form.elements[key].value = value || ''; });
                submit.textContent = 'Retry same order';
                status.textContent = 'A previous submission needs confirmation. Retry the same order safely.';
                return;
            }
            await loadQuote();
        } catch { showError('Browser storage is unavailable or invalid. Enable storage and reload checkout.'); }
    }
    init();
    window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
})();
