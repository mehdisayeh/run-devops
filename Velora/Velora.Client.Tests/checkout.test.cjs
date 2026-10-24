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
    const local = options.local || storage({ [cartKey]: JSON.stringify([{ id: 1, quantity: 2 }]) });
    const session = options.session || storage();
    const requests = [], redirects = [];
    runInNewContext(source, {
        document: { getElementById: get, createElement: node },
        window: { addEventListener() {} }, Intl, crypto: webcrypto,
        sessionStorage: session, localStorage: local,
        location: { replace: value => redirects.push(value), reload() {} },
        fetch: async (url, request) => {
            requests.push({ url, headers: request.headers, mode: request.mode, body: JSON.parse(request.body) });
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

test('checkout sends only items and address, deducts the bag, and opens receipt', async () => {
    const app = setup([{ body: quote }, { body: receipt }]);
    await app.settle();
    await app.submit();
    const sent = app.requests[1];
    assert.deepEqual(Object.keys(sent.body).sort(), ['address', 'items']);
    assert.equal(sent.headers['X-Checkout-Request'], undefined);
    assert.equal(sent.headers['X-CSRF-TOKEN'], undefined);
    assert.deepEqual(JSON.parse(app.local.getItem(cartKey)), []);
    assert.equal(app.redirects[0], `/Checkout/Confirmation?id=${receipt.id}`);
});

test('insufficient stock displays error and allows refreshing the quote', async () => {
    const app = setup([{ body: quote }, { status: 409, body: { detail: 'Not enough stock' } }]);
    await app.settle(); await app.submit();
    assert.equal(app.get('checkout-error').textContent, 'Not enough stock');
    assert.equal(app.get('refresh-quote').hidden, false);
    assert.equal(JSON.parse(app.local.getItem(cartKey)).length, 1);
});

test('network failure keeps bag and does not automatically retry', async () => {
    const app = setup([{ body: quote }, new Error('Connection lost')]);
    await app.settle(); await app.submit();
    assert.equal(app.requests.length, 2);
    assert.equal(app.get('place-order').disabled, true);
    assert.match(app.get('checkout-status').textContent, /unknown/);
    assert.equal(JSON.parse(app.local.getItem(cartKey)).length, 1);
});

test('checkout works without session storage', async () => {
    const app = setup([{ body: quote }, { body: receipt }], { session: { getItem() { throw new Error('Disabled'); }, setItem() { throw new Error('Disabled'); } } });
    await app.settle(); await app.submit();
    assert.equal(app.redirects.length, 1);
});

test('empty bag cannot be submitted', async () => {
    const app = setup([], { local: storage() });
    await app.settle(); await app.submit();
    assert.equal(app.requests.length, 0);
    assert.match(app.get('checkout-error').textContent, /bag is empty/);
});
