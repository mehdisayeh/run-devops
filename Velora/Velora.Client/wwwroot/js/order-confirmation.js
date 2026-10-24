(() => {
    try {
        const key = 'velora.checkout.pending.v1';
        const pending = JSON.parse(sessionStorage.getItem(key) || 'null');
        if (pending?.receiptId === document.getElementById('order-confirmation')?.dataset.orderId)
            sessionStorage.removeItem(key);
    } catch { /* Reading an existing receipt does not require storage. */ }
})();
