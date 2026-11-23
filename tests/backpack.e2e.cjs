// Fixture isolado no navegador. Requer playwright-core e Chromium.
const {chromium} = require('playwright-core');
const assert = require('node:assert/strict');
const main = process.env.ULTIMA_MODE === 'main';
const fixture = {kind:'place', uid:'test-backpack-stump', asset:'prop.stump', x:0, z:2, rot:0, scale:1};
const site = process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/';
const touch = process.env.ULTIMA_TOUCH === '1';
const url = site+'?live=0&rede=0&api=http%3A%2F%2F127.0.0.1%3A19999'+(main?'&render=main&gpu=0':'');
const scene = page => main ? page : page.workers()[0];
async function project(target, point) {
    return target.evaluate(({name,x,z})=>{
        const s = BABYLON.EngineStore.LastCreatedScene;
        const engine = s.getEngine();
        const world = name ? s.getMeshByName(name).getBoundingInfo().boundingBox.centerWorld :
            s.pickWithRay(new BABYLON.Ray(new BABYLON.Vector3(x,100,z),new BABYLON.Vector3(0,-1,0)),m=>m.name.startsWith('ground-')).pickedPoint;
        const viewport = s.activeCamera.viewport.toGlobal(engine.getRenderWidth(),engine.getRenderHeight());
        const screen = BABYLON.Vector3.Project(world,BABYLON.Matrix.Identity(),s.getTransformMatrix(),viewport);
        return {x:screen.x,y:screen.y,alt:world.y};
    },point);
}
async function bag(page) {
    await page.getByRole('button',{name:'Abrir painel do jogo'}).click();
    await page.getByText('Mochila',{exact:true}).click();
}
async function ready(page) {
    await page.goto(url);
    await until(()=>page.evaluate(async()=>{
        if (!window.game?.glb?.status || !game?.build?.state) return false;
        const map=await game.build.state(), glb=await game.glb.status();
        return map?.chunks>=25 && glb?.loaded.some(id=>id.startsWith('prop.stump')) && (await game.models.status()).queue===0;
    }));
}
async function records(page) {
    return page.evaluate(()=>new Promise((resolve,reject)=>{
        const db=indexedDB.open('ultima-backpack',1);
        db.onerror=()=>reject(db.error);
        db.onsuccess=()=>{
            const req=db.result.transaction('collected').objectStore('collected').getAll();
            req.onsuccess=()=>{db.result.close();resolve(req.result)};
            req.onerror=()=>reject(req.error);
        };
    }));
}
async function until(check) {
    for (let i=0;i<100;i++) {
        if (await check()) return;
        await new Promise(resolve=>setTimeout(resolve,250));
    }
    throw Error('A alteração esperada não apareceu');
}
(async()=>{
    const browser=await chromium.launch({executablePath:process.env.ULTIMA_CHROMIUM || undefined,
        args:(process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean)});
    try {
        const context=await browser.newContext({viewport:{width:1200,height:720},hasTouch:touch});
        const page=await context.newPage();
        const errors=[];
        page.on('pageerror',e=>errors.push(String(e)));
        await ready(page);
        assert.equal(await page.evaluate(()=>game.mode), main?'main':'worker');
        const build=await page.locator('meta[name=ultima-build]').getAttribute('content');
        if (process.env.ULTIMA_BUILD) assert.equal(build,process.env.ULTIMA_BUILD);
        const target=await scene(page);
        const mesh='place-'+fixture.uid;
        const enabled=target=>target.evaluate(n=>BABYLON.EngineStore.LastCreatedScene.getMeshByName(n)?.isEnabled(),mesh);
        const place=page=>page.evaluate(op=>game.applyWorldOp(op),fixture);
        await place(page);
        await until(()=>enabled(target));
        await bag(page);
        await page.getByText('Mochila vazia.',{exact:true}).waitFor();
        await page.getByRole('button',{name:'Coletar',exact:true}).click();
        const click=async point=>{
            const screen=await project(target,point);
            await until(()=>page.evaluate(({x,y})=>document.elementFromPoint(x,y)?.id==='game-canvas',screen));
            if (touch) await page.touchscreen.tap(screen.x,screen.y);
            else await page.mouse.click(screen.x,screen.y);
            return screen;
        };
        await click({name:mesh});
        await page.getByText('+1 Toco',{exact:true}).waitFor({timeout:10000});
        await until(async()=>await enabled(target)===false);
        await click({x:1,z:9});
        await page.getByText('Aproxime-se para coletar.',{exact:true}).waitFor({timeout:10000});
        const soil={x:1,z:1};
        const before=(await click(soil)).alt;
        await until(async()=>(await records(page)).some(r=>r.kind==='soil'));
        await until(async()=>before-(await project(target,soil)).alt>0.3);
        const after=(await click(soil)).alt;
        await page.getByText('Esse recurso já foi coletado.',{exact:true}).waitFor({timeout:10000});
        const saved=await records(page);
        assert.deepEqual(saved.map(record=>record.item),['Toco','Terra']);
        assert.ok(Math.abs(before-after-0.48)<0.04);
        await page.getByRole('button',{name:'Parar coleta',exact:true}).click();
        await bag(page);
        for (const record of saved) await page.getByText(record.item,{exact:true}).waitFor();
        await ready(page);
        const reloaded=await scene(page);
        await place(page);
        await until(async()=>await enabled(reloaded)===false);
        assert.ok(Math.abs((await project(reloaded,soil)).alt-after)<0.04);
        await bag(page);
        for (const record of saved) await page.getByText(record.item,{exact:true}).waitFor();
        const second=await context.newPage();
        await ready(second);
        await Promise.all([page,second].map(p=>p.evaluate(r=>game.backpack.collect(r),
            {id:'concurrency-check',item:'Validação de duplicidade',kind:'object'})));
        assert.equal((await records(page)).length,3);
        await page.getByText('Validação de duplicidade',{exact:true}).waitFor();
        assert.deepEqual(errors,[]);
        console.log({build, mode:main?'main':'worker', touch, items:saved.map(record=>record.item),
            depth:before-after, reload:true, atomicAcrossTabs:true});
    } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1});
