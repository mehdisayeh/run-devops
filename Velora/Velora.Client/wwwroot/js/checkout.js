(() => {
    'use strict';
    const form = document.getElementById('checkout-form');
    if (!form) return;
    const status = document.getElementById('checkout-status');
    const error = document.getElementById('checkout-error');
    const submit = document.getElementById('place-order');
    const fields = document.getElementById('checkout-fields');
    const refresh = document.getElementById('refresh-quote');
    const cartKey = 'forme.cart.v1';
    const money = value => new Intl.NumberFormat('en-IE', { style: 'currency', currency: 'EUR' }).format(value);
    let quote, items, busy = false;
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
            method: 'POST', mode: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        let data;
        try { data = await response.json(); } catch { data = {}; }
        if (!response.ok) {
            const failure = new Error(data.detail || Object.values(data.errors || {}).flat().join(' ') || 'Checkout could not complete. Please try again.');
            failure.status = response.status;
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
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || !quote || !form.reportValidity()) return;
        busy = true;
        submit.disabled = true;
        fields.disabled = true;
        error.hidden = true;
        status.textContent = 'Placing your order?';
        const address = Object.fromEntries(['fullName', 'email', 'line1', 'line2', 'city', 'postalCode', 'country']
            .map(name => [name, form.elements[name].value.trim()]));
        try {
            const receipt = await post('Place', { items, address });
            const cart = JSON.parse(localStorage.getItem(cartKey) || '[]');
            const remaining = cart.map(item => ({ ...item, quantity: item.quantity -
                (items.find(line => line.productId === item.id)?.quantity || 0) })).filter(item => item.quantity > 0);
            try { localStorage.setItem(cartKey, JSON.stringify(remaining)); } catch { /* Still show the saved order. */ }
            location.replace(`/Checkout/Confirmation?id=${encodeURIComponent(receipt.id)}`);
        } catch (failure) {
            showError(failure.message);
            if (failure.status === 400 || failure.status === 409 || failure.status === 422) {
                fields.disabled = false;
                refresh.hidden = false;
                status.textContent = 'Update your bag or details and try again.';
            } else {
                // No automatic retries: the server may have saved the order.
                status.textContent = 'Order outcome is unknown. Check with the store before submitting again.';
            }
        } finally { busy = false; }
    });
    refresh.addEventListener('click', loadQuote);
    loadQuote();
})();
