// Run with: node --test Velora/Velora.Client.Tests/checkout.test.cjs
// Exercises the shipped checkout script with a minimal DOM and controllable transport.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');
const { webcrypto } = require('node:crypto');
const source = readFileSync(join(__dirname, '../Velora.Client/wwwroot/js/checkout.js'), 'utf8');
const cartKey = 'forme.cart.v1', pendingKey = 'velora.checkout.pending.v1';
const quote = { items: [{ productId: 1, name: 'Product', quantity: 2, unitPrice: 10, lineTotal: 20 }], total: 20, version: 'a'.repeat(64) };
const receipt = { id: 'b'.repeat(64) };
function storage(initial = {}) {
    const values = new Map(Object.entries(initial));
    return { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value), removeItem: key => values.delete(key) };
}
function node() {
    return { hidden: false, disabled: false, textContent: '', value: 'Test', children: [], events: {},
        append(...children) { this.children.push(...children); }, replaceChildren() { this.children = []; },
        addEventListener(event, callback) { this.events[event] = callback; } };
}
function setup(responses, options = {}) {
    const nodes = new Map();
    const get = id => { if (!nodes.has(id)) nodes.set(id, node()); return nodes.get(id); };
    const form = get('checkout-form');
    form.elements = Object.fromEntries(['fullName', 'email', 'line1', 'line2', 'city', 'postalCode', 'country'].map(key => [key, node()]));
    form.reportValidity = () => true;
    form.querySelector = () => ({ value: 'csrf-test' });
    const local = options.local || storage({ [cartKey]: JSON.stringify([{ id: 1, quantity: 2 }]) });
    const session = options.session || storage();
    const requests = [], redirects = [];
    runInNewContext(source, {
        document: { getElementById: get, createElement: node },
        window: { addEventListener() {} }, Intl, crypto: webcrypto,
        sessionStorage: session, localStorage: local,
        location: { replace: value => redirects.push(value), reload() {} },
        fetch: async (url, request) => {
            requests.push({ url, body: JSON.parse(request.body) });
            const response = responses.shift();
            if (response instanceof Error) throw response;
            if (!response) throw new Error('Unexpected request');
            return { ok: !response.status || response.status === 200, status: response.status || 200,
                headers: { get: () => response.definitive ? 'true' : null }, json: async () => response.body };
        }
    });
    return { get, form, local, session, requests, redirects,
        submit: () => form.events.submit({ preventDefault() {} }),
        settle: () => new Promise(resolve => setImmediate(resolve)) };
}

test('successful order clears only purchased quantities and saves receipt before redirect', async () => {
    const app = setup([{ body: quote }, { body: receipt }]);
    await app.settle();
    // Preserve products added in another tab after the reviewed quote.
    app.local.setItem(cartKey, JSON.stringify([{ id: 1, quantity: 3 }, { id: 2, quantity: 1 }]));
    await app.submit();
    assert.equal(app.requests[1].body.expectedTotal, 20);
    assert.equal(app.requests[1].body.quoteVersion, quote.version);
    assert.match(app.requests[1].body.idempotencyKey, /^[a-f0-9]{32}$/);
    assert.deepEqual(JSON.parse(app.local.getItem(cartKey)), [{ id: 1, quantity: 1 }, { id: 2, quantity: 1 }]);
    assert.equal(JSON.parse(app.session.getItem(pendingKey)).receiptId, receipt.id);
    assert.equal(app.redirects[0], `/Checkout/Confirmation?id=${receipt.id}`);
});

test('network failure freezes payload and retries exactly the same key and address', async () => {
    const app = setup([{ body: quote }, new Error('Connection lost'), { body: receipt }]);
    await app.settle(); await app.submit();
    assert.equal(app.get('checkout-fields').disabled, true);
    assert.equal(app.get('place-order').textContent, 'Retry same order');
    app.form.elements.city.value = 'Changed after submission';
    await app.submit();
    assert.deepEqual(app.requests[1].body, app.requests[2].body);
});

test('reload after uncertain submission restores request without fetching a new quote', async () => {
    const first = setup([{ body: quote }, new Error('Connection lost')]);
    await first.settle(); await first.submit();
    const next = setup([{ body: receipt }], { session: first.session, local: first.local });
    await next.settle();
    assert.equal(next.requests.length, 0);
    assert.equal(next.get('place-order').textContent, 'Retry same order');
    await next.submit();
    assert.deepEqual(next.requests[0].body, first.requests[1].body);
});

test('definitive quote conflict requires fresh review before another submission', async () => {
    const app = setup([{ body: quote }, { status: 409, definitive: true, body: { detail: 'Stock changed' } }]);
    await app.settle(); await app.submit();
    assert.equal(app.session.getItem(pendingKey), null);
    assert.equal(app.get('place-order').disabled, true);
    assert.equal(app.get('refresh-quote').hidden, false);
    assert.equal(app.get('checkout-error').textContent, 'Stock changed');
});

test('middleware errors never discard an uncertain order submission key', async () => {
    const app = setup([{ body: quote }, { status: 400, body: {} }]);
    await app.settle(); await app.submit();
    assert.ok(JSON.parse(app.session.getItem(pendingKey)).request.idempotencyKey);
    assert.equal(app.get('place-order').textContent, 'Retry same order');
});

test('empty bag cannot be submitted', async () => {
    const app = setup([], { local: storage() });
    await app.settle(); await app.submit();
    assert.equal(app.requests.length, 0);
    assert.match(app.get('checkout-error').textContent, /bag is empty/);
});

test('session storage failure prevents order submission', async () => {
    const broken = { ...storage(), setItem() { throw new Error('Storage disabled'); } };
    const app = setup([], { session: broken });
    await app.settle(); await app.submit();
    assert.equal(app.requests.length, 0);
    assert.match(app.get('checkout-error').textContent, /storage is unavailable/);
});
