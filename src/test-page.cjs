// Offline tests of the exact page adapter, without any live browser or Amazon requests.
const {readFileSync,writeFileSync} = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = readFileSync(__dirname+'/page.js','utf8');
const passed=[];
function element(text='',visible=true) {return {innerText:text,textContent:text,parentElement:null,getClientRects:()=>visible?[{}]:[],getAttribute:()=>null,closest:()=>null,click(){this.clicked=true;},dispatchEvent(e){this.events=(this.events||[]).concat(e.type);}};}
function run(action,entries={},body='') {
 const context={ACTION:action,PIN:'400001',document:{body:{innerText:body},readyState:'complete',querySelectorAll:s=>entries[s]||[],querySelector:s=>(entries[s]||[])[0]||null},window:{location:{href:'https://www.amazon.in/dp/B0B5V5VHB6'}},getComputedStyle:()=>({visibility:'visible'}),HTMLInputElement:class {set value(v){this.valueSet=v;}},Event:class{constructor(type){this.type=type;}}};
 return vm.runInNewContext(source,context);
}
function test(name,fn){fn();passed.push('PASS '+name);}
test('Reads current location from whole header (including signed-out layout)',()=>{const s=run('read',{'#nav-global-location-popover-link':[element('Delivering to Noida 201304 Update location')]});assert.match(s.location,/201304/);});
test('Reads the newer deliveryBlockContainer',()=>{assert.match(run('read',{'#deliveryBlockContainer':[element('Cannot be shipped')]}).delivery,/Cannot be shipped/);});
test('Ignores hidden delivery evidence',()=>{assert.equal(run('read',{'#deliveryBlockContainer':[element('FREE delivery Monday',false)]}).delivery,'');});
test('Reads qualified buybox negative evidence',()=>{assert.equal(run('read',{'#desktop_qualifiedBuyBox':[element('cannot be shipped')]}).buybox,'cannot be shipped');});
test('Price extraction uses visible price parent',()=>{const p=element('₹3,119.00');p.parentElement=element();assert.equal(run('read',{'#corePriceDisplay_desktop_feature_div .a-price:not(.a-text-price) .a-offscreen':[p]}).price,'₹3,119.00');});
test('Does not accept disabled cart',()=>{const c=element();c.disabled=true;assert.equal(run('read',{'#add-to-cart-button':[c]}).cart,false);});
test('Does not accept disabled wrapper',()=>{const c=element();c.closest=()=>({});assert.equal(run('read',{'#add-to-cart-button':[c]}).cart,false);});
test('Accepts enabled visible cart',()=>{assert.equal(run('read',{'#add-to-cart-button':[element()]}).cart,true);});
test('Detects human-check page',()=>{assert.equal(run('read',{},'Enter the characters you see').blocked,true);});
test('Does not click location while human check is present',()=>{const e=element();const s=run('open',{'#nav-global-location-popover-link, #contextualIngressPtLink':[e]},'Robot check');assert.equal(s.ok,false);assert.equal(e.clicked,undefined);});
test('Sets requested pincode and dispatches input and change',()=>{const i=element(),b=element();assert.equal(run('apply',{'#GLUXZipUpdateInput':[i],'#GLUXZipUpdate input, #GLUXZipUpdate button, #GLUXZipUpdate .a-button-input':[b]}).ok,true);assert.equal(i.valueSet,'400001');assert.deepEqual(i.events,['input','change']);assert.equal(b.clicked,true);});
test('Missing apply form produces a controlled failure',()=>{assert.equal(run('apply').ok,false);});
writeFileSync(__dirname+'/../results/page-tests.txt',passed.join('\n')+'\n');console.log(passed.join('\n'));
