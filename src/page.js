(() => {
  const visible = e => !!e && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden';
  const one = s => [...document.querySelectorAll(s)].find(visible);
  const text = s => { const e = one(s); return e ? e.innerText.trim() : ''; };
  const body = document.body ? document.body.innerText : '';
  const blocked = /enter the characters you see|type the characters|robot check|verify you are human|automated access|sorry, we just need to make sure/i.test(body) || !!document.querySelector('#captchacharacters');
  if (ACTION === 'open') {
    const e = one('#nav-global-location-popover-link, #contextualIngressPtLink');
    if (e && !blocked) { e.click(); return {ok:true}; }
    return {ok:false, blocked};
  }
  if (ACTION === 'apply') {
    const input = one('#GLUXZipUpdateInput');
    const button = one('#GLUXZipUpdate input, #GLUXZipUpdate button, #GLUXZipUpdate .a-button-input');
    if (!input || !button || blocked) return {ok:false, blocked};
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, PIN);
    input.dispatchEvent(new Event('input', {bubbles:true}));
    input.dispatchEvent(new Event('change', {bubbles:true}));
    button.click(); return {ok:true};
  }
  if (ACTION === 'close') {
    const e = one('#GLUXConfirmClose, #GLUXConfirmClose-announce, .a-popover-footer .a-button-primary input, button[name="glowDoneButton"]');
    if (e) e.click();
    return {ok:!!e};
  }
  const location = text('#nav-global-location-popover-link') + ' ' + text('#contextualIngressPtLabel');
  const delivery = ['#deliveryBlockContainer', '#deliveryBlockMessage', '#mir-layout-DELIVERY_BLOCK', '#deliveryMessageMirId', '#mir-layout-DELIVERY_BLOCK-slot-PRIMARY_DELIVERY_MESSAGE_LARGE', '#mir-layout-DELIVERY_BLOCK-slot-SECONDARY_DELIVERY_MESSAGE_LARGE'].map(text).filter(Boolean).join(' ');
  const availability = text('#availability');
  const buybox = text('#desktop_qualifiedBuyBox') || text('#desktop_buybox') || text('#buybox');
  const prices = ['#corePriceDisplay_desktop_feature_div .a-price:not(.a-text-price) .a-offscreen', '#corePrice_feature_div .a-price:not(.a-text-price) .a-offscreen', '#apex_desktop .a-price:not(.a-text-price) .a-offscreen', '#priceblock_ourprice', '#priceblock_dealprice'];
  // Amazon's accessible price span is intentionally visually clipped; inspect its visible parent.
  let price = '';
  for (const s of prices) { const e = [...document.querySelectorAll(s)].find(e => visible(e.parentElement)); if (e) {price = e.textContent.trim(); break;} }
  const cart = one('#add-to-cart-button');
  return {
    blocked, href:locationHost(), title:text('#productTitle'), location, delivery, availability, price,
    buybox:buybox.slice(0,5000), cart:!!cart && !cart.disabled && cart.getAttribute('aria-disabled') !== 'true' && !cart.closest('.a-button-disabled'),
    invalid:text('#GLUXZipError, #GLUXZipErrorMessage, .GLUX_Hidden .a-alert-content'),
    ready:document.readyState === 'complete', body:ACTION === 'probe' ? body.slice(0,22000) : ''
  };
  function locationHost() {return window.location.href;}
})()
