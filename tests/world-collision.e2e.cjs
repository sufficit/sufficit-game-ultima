// Fixture local ao navegador. Requer playwright-core e Chromium.
// ULTIMA_SITE pode apontar para um publish local. ULTIMA_MODE=main exercita o fallback.
const {chromium}=require('playwright-core');
const assert=require('node:assert/strict');
const site=process.env.ULTIMA_SITE || 'https://sufficit.github.io/sufficit-game-ultima/';
const main=process.env.ULTIMA_MODE==='main';
// ULTIMA_WALK_SCALE alonga as janelas de caminhada/assentamento (padrão 1 = comportamento
// original). Runners com renderização por software (SwiftShader sem GPU) podem rodar a poucos
// quadros por segundo; segurar a tecla por mais tempo percorre a mesma distância sem alterar
// as condições de colisão. ULTIMA_VIEWPORT reduz o custo de raster (padrão 1200x720).
const walkScale=Number(process.env.ULTIMA_WALK_SCALE)||1;
const T300=Math.round(300*walkScale);
const [vw,vh]=(process.env.ULTIMA_VIEWPORT||'1200x720').split('x').map(Number);
const fixture={kind:'place',uid:'browser-tree',asset:'flora.pine',x:0,z:2,rot:0,scale:1};
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
async function wait(callback,label) {
    // ULTIMA_WAIT_TRIES amplia o orçamento de espera (padrão 100 = 25 s); runners sob
    // carga alta (ex.: bancada compartilhada com load ~30) podem precisar de mais tempo
    // para carregar GLBs+modelos sem enfraquecer a condição do gate.
    const tries=Number(process.env.ULTIMA_WAIT_TRIES)||100;
    for(let i=0;i<tries;i++){
        const v=await callback();
        if(v) return v;
        await sleep(250);
    }
    throw Error(label);
}
(async()=>{
 const browser=await chromium.launch({executablePath:process.env.ULTIMA_CHROMIUM || undefined,
    args:(process.env.ULTIMA_CHROMIUM_ARGS || '').split(' ').filter(Boolean)});
 try{
    const p=await browser.newPage({viewport:{width:vw,height:vh}});
    const errors=[];
    p.on('pageerror',e=>errors.push(String(e)));
    const url=site+'?live=0&rede=0&api=http%3A%2F%2F127.0.0.1%3A19999'+(main?'&render=main&gpu=0':'');
    await p.goto(url,{waitUntil:'domcontentloaded'});
    const build=await p.locator('meta[name=ultima-build]').getAttribute('content');
    if(process.env.ULTIMA_BUILD) assert.equal(build, process.env.ULTIMA_BUILD);
    await wait(()=>p.evaluate(async()=>{
        if(!window.game?.glb?.status || !window.game?.build?.state) return false;
        const map=await game.build.state(), glb=await game.glb.status(), models=await game.models.status();
        return map?.chunks>=25 && glb?.loaded.some(id=>id.startsWith('flora.pine')) &&
            glb?.loaded.some(id=>id.startsWith('prop.stump')) &&
            glb?.loaded.some(id=>id.startsWith('prop.log')) &&
            glb?.loaded.some(id=>id.startsWith('rock.boulder2')) && models.queue===0 &&
            models.loaded.includes('character.villager v2');
    }),'mundo carregado');
    assert.equal(await p.evaluate(()=>game.mode),main?'main':'worker');
    const renderer=main ? p : p.workers()[0];
    await p.getByRole('button',{name:'detalhes',exact:true}).click();
    await p.locator('#game-canvas').focus();
    const pose=async()=> {
        if(main) return p.evaluate(()=>{
            const {x,y,z}=BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('player').position;
            return {x,y,z};
        });
        const status=await p.locator('.ultima-status').innerText();
        const match=/pos \((-?[\d.,]+), (-?[\d.,]+)\) · alt (-?[\d.,]+)/.exec(status);
        assert.ok(match, status);
        return {x:Number(match[1].replace(',','.')),z:Number(match[2].replace(',','.')),y:Number(match[3].replace(',','.'))};
    };
    const apply=op=>p.evaluate(op=>game.applyWorldOp(op),op);
    async function walk(keys,ms,sprint=true){const hold=Math.round(ms*walkScale);
        if(sprint) await p.keyboard.down('Shift');
        for(const key of keys) await p.keyboard.down(key);
        await p.waitForTimeout(hold);
        for(const key of keys) await p.keyboard.up(key);
        if(sprint) await p.keyboard.up('Shift');
        await p.waitForTimeout(T300);
        if(errors.length) throw Error(errors.join('\n'));
        return pose();
    }
    // Mede a velocidade em TEMPO DE JOGO (soma do delta truncado por quadro), não em tempo de parede:
    // sob renderização por software o runner faz ~1 quadro/s e cada quadro avança no máximo 0,05 s
    // (game-core.js), então a distância percorrida em tempo de parede depende do FPS da janela.
    // A razão entre caminhada e corrida preserva a mesma condição em qualquer runner.
    async function speed(keys,ms,sprint=true,turn=null){
        const hold=Math.round(ms*walkScale);
        await renderer.evaluate(() => {
            const scene=BABYLON.EngineStore.LastCreatedScene, engine=BABYLON.EngineStore.LastCreatedEngine;
            const player=scene.getTransformNodeByName('player');
            const samples=[];
            const ankles=['left','right'].map(side=>scene.getTransformNodeByName('player-ankle-'+side));
            const observer=scene.onAfterRenderObservable.add(() => {
                const feet=ankles.map(ankle=>{
                    const direction=ankle.getDirection(BABYLON.Axis.Z);
                    const shoe=scene.getMeshByName(ankle.name.replace('ankle','shoe'));
                    const {x,z}=shoe.getAbsolutePosition();
                    const pitch=Math.asin(Math.max(-1,Math.min(1,-direction.y)));
                    return {x,z,pitch,planted:Math.abs(pitch)<1e-6};
                });
                samples.push({x:player.position.x,z:player.position.z,g:Math.min(0.05,engine.getDeltaTime()/1000),yaw:player.rotation.y,feet});
            });
            globalThis.stopMoveTrace=() => { scene.onAfterRenderObservable.remove(observer); return samples; };
        });
        if(sprint) await p.keyboard.down('Shift');
        let samples=null;
        for(const key of keys) await p.keyboard.down(key);
        try {
            if (turn) {
                await p.waitForTimeout(hold/2);
                for(const key of keys) await p.keyboard.up(key);
                keys=[turn];
                await p.keyboard.down(turn);
            }
            await p.waitForTimeout(turn ? hold/2 : hold);
        }
        finally {
            for(const key of keys) await p.keyboard.up(key);
            if(sprint) await p.keyboard.up('Shift');
            // Curvas e inversões também medem o apoio depois da frenagem.
            if (turn) await p.waitForTimeout(T300);
            samples=await renderer.evaluate(() => globalThis.stopMoveTrace());
        }
        await p.waitForTimeout(T300);
        if(errors.length) throw Error(errors.join('\n'));
        if(process.env.ULTIMA_TRACE){ // diagnóstico: [delta de jogo, distância] por intervalo
            console.log('TRACE',JSON.stringify(samples.map((s,i)=>[+s.g.toFixed(3),i?+Math.hypot(s.x-samples[i-1].x,s.z-samples[i-1].z).toFixed(3):null])));
        }
        // Descarta a rampa de aceleração (primeiros 0,3 s de jogo): numerador e denominador
        // precisam ser PARES — só contam os passos após a rampa (tempo gC = tempo dos passos medidos).
        let g=0, gC=0, dist=0, n=0, plantedFrames=0, maxSlide=0, turningFrames=0, turnSlide=0;
        for(let i=1;i<samples.length;i++){
            const d=Math.hypot(samples[i].x-samples[i-1].x, samples[i].z-samples[i-1].z);
            if(g>0.3){
                dist+=d; gC+=samples[i].g; n++;
                if(d>0.02) for(let foot=0;foot<2;foot++){
                    const a=samples[i-1].feet[foot], b=samples[i].feet[foot];
                    if(a.planted && b.planted && d<0.2){
                        plantedFrames++;
                        const slide=Math.hypot(b.x-a.x,b.z-a.z);
                        maxSlide=Math.max(maxSlide,slide);
                        if(Math.abs(samples[i].yaw-samples[i-1].yaw)>0.01){
                            turningFrames++;
                            turnSlide=Math.max(turnSlide,slide);
                        }
                    }
                }
            }
            g+=samples[i].g;
        }
        const last=samples[samples.length-1];
        if(n<3){ // poucos quadros: usa a janela inteira (pareada) como fallback
            const gAll=samples.slice(1).reduce((a,s)=>a+s.g,0);
            const dAll=samples.length>1?Math.hypot(last.x-samples[0].x,last.z-samples[0].z):0;
            return {v:gAll>0?dAll/gAll:0,z:last?.z,frames:samples.length};
        }
        let stopFrames=0, stopSlide=0;
        for(let i=samples.length-1;i>0;i--){
            const a=samples[i-1], b=samples[i];
            if(Math.hypot(b.x-a.x,b.z-a.z)>1e-6) break;
            for(let foot=0;foot<2;foot++){
                if(a.feet[foot].planted && b.feet[foot].planted){
                    stopFrames++;
                    stopSlide=Math.max(stopSlide,Math.hypot(b.feet[foot].x-a.feet[foot].x,b.feet[foot].z-a.feet[foot].z));
                }
            }
        }
        const pitches=samples.flatMap(sample=>sample.feet.map(foot=>foot.pitch));
        return {v:gC>0?dist/gC:0,z:last?.z,frames:samples.length,plantedFrames,maxSlide,
            minPitch:Math.min(...pitches),maxPitch:Math.max(...pitches),turningFrames,turnSlide,stopFrames,stopSlide};
    }
    const walking=await speed(['w'],1800,false);
    assert.ok(walking.plantedFrames>=5 && walking.maxSlide<0.012,
        'pé de apoio deve ficar fixo no mundo durante a marcha em linha reta: '+JSON.stringify(walking));
    assert.ok(walking.minPitch < -0.1 && walking.maxPitch > 0.1, 'tornozelo alterna a inclinação durante o balanço');
    assert.ok(await renderer.evaluate(()=>{
        const scene=BABYLON.EngineStore.LastCreatedScene;
        const node=name=>scene.getTransformNodeByName('player-'+name);
        const joint=(child,parent)=>!!node(child) && node(child).parent===node(parent);
        return joint('ankle-left','knee-left') && joint('wrist-right','elbow-right') &&
            joint('toe-1-left-tip','toe-1-left');
    }),'jogador instancia o esqueleto humano');
    await p.evaluate(()=>game.resetPlayer());
    await p.waitForTimeout(T300);
    const turning=await speed(['w'],1500,false,'d');
    console.log(JSON.stringify({turning}));
    assert.ok(turning.turningFrames>=2 && turning.turnSlide<0.012,
        'pé de apoio deve ficar no mesmo lugar durante a curva: '+JSON.stringify(turning));
    await p.evaluate(()=>game.resetPlayer());
    await p.waitForTimeout(T300);
    const reversing=await speed(['w'],1500,false,'s');
    console.log(JSON.stringify({reversing}));
    assert.ok(reversing.turningFrames>=2 && reversing.turnSlide<0.012,
        'pé de apoio deve ficar no mesmo lugar durante a inversão: '+JSON.stringify(reversing));
    assert.ok(reversing.stopFrames>=2 && reversing.stopSlide<0.012,
        'pé em apoio deve permanecer imóvel ao parar: '+JSON.stringify(reversing));
    await p.evaluate(()=>game.resetPlayer());
    await p.waitForTimeout(T300);
    const running=await speed(['w'],1400);
    console.log(JSON.stringify({walking,running}));
    assert.ok(walking.v>2.2 && running.v>walking.v*1.45, 'Shift distingue caminhada e corrida');
    await p.evaluate(()=>game.resetPlayer());
    await apply(fixture);
    await p.waitForTimeout(T300);
    const blocked=await walk(['w'],1150);
    console.log(JSON.stringify({build,mode:main?'main':'worker',blocked}));
    assert.ok(blocked.z>1.28 && blocked.z<1.61,'o tronco deve parar o jogador em z≈1,47');
    await p.locator('.ultima-hint').getByText('para examinar',{exact:false}).waitFor();
    await p.keyboard.press('e');
    await p.getByText('Você examinou: Pinheiro',{exact:true}).waitFor();
    await wait(()=>renderer.evaluate(()=>BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('player-upper-body').rotation.x>0.10),
        'o jogador se inclina ao examinar');
    const slide=await walk(['w','d'],450);
    assert.ok(slide.x>0.45 && slide.z>2, 'desliza pela lateral '+JSON.stringify(slide));
    await apply({kind:'remove',uid:fixture.uid});
    await p.evaluate(()=>game.resetPlayer());
    await p.waitForTimeout(T300);
    const free=await walk(['w'],820);
    assert.ok(free.z>2.65, 'remoção libera o caminho');
    await p.evaluate(()=>game.resetPlayer());
    await apply({...fixture,uid:'browser-soft',asset:'flora.fern'});
    await p.waitForTimeout(T300);
    const soft=await walk(['w'],850);
    assert.ok(soft.z>2.65,'samambaia permite passagem');
    await apply({kind:'remove',uid:'browser-soft'});
    await p.evaluate(()=>game.resetPlayer());
    await apply({...fixture,uid:'browser-stump',asset:'prop.stump',scale:1.45});
    await p.waitForTimeout(T300);
    const stump=await walk(['w'],850);
    assert.ok(stump.z>0.7 && stump.z<1.55,'toco sólido '+JSON.stringify(stump));
    await apply({kind:'remove',uid:'browser-stump'});
    await p.evaluate(()=>game.resetPlayer());
    await apply({...fixture,uid:'browser-rock',asset:'rock.boulder2'});
    await p.waitForTimeout(T300);
    const rock=await walk(['w'],850);
    assert.ok(rock.z>0.45 && rock.z<1.7,'pedra sólida '+JSON.stringify(rock));
    await apply({kind:'remove',uid:'browser-rock'});

    await p.evaluate(()=>game.resetPlayer());
    await apply({...fixture,uid:'browser-log',asset:'prop.log',scale:0.56});
    await p.waitForTimeout(Math.round(400*walkScale));
    // Registra cada quadro no renderizador para captar o apoio curto em corrida.
    await renderer.evaluate(()=>{
        const scene=BABYLON.EngineStore.LastCreatedScene;
        const player=scene.getTransformNodeByName('player');
        const knees=['left','right'].map(side=>scene.getTransformNodeByName('player-knee-'+side));
        const ankles=['left','right'].map(side=>scene.getTransformNodeByName('player-ankle-'+side));
        const samples=[];
        const observer=scene.onAfterRenderObservable.add(()=>{
            const {z}=player.position;
            const heights=ankles.map(ankle=>ankle.getAbsolutePosition().y);
            samples.push({z,ankleY:Math.max(...heights),bend:Math.max(...knees.map(knee=>knee.rotation.x)),
                feetDifference:Math.abs(heights[0]-heights[1])});
        });
        globalThis.stopStepTrace=()=>{
            scene.onAfterRenderObservable.remove(observer);
            return samples;
        };
    });
    await p.keyboard.down('w');
    try {
        // Espera por posição (main remota 2bb2efe), não por tempo de parede: independe do FPS do runner.
        await wait(()=>renderer.evaluate(() =>
            BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('player').position.z > 3.2),
            'o jogador não passou do tronco');
    } finally {
        await p.keyboard.up('w');
    }
    await p.waitForTimeout(T300);
    const samples=await renderer.evaluate(()=>globalThis.stopStepTrace());
    const pastLog=await pose();
    const overLog=samples.filter(sample=>sample.z>1.4 && sample.z<2.6);
    const step={
        anklePeak:Math.max(...overLog.map(sample=>sample.ankleY)),
        flex:Math.max(...overLog.map(sample=>sample.bend)),
        feetDifference:Math.max(...overLog.map(sample=>sample.feetDifference)),
        end:pastLog,
    };
    console.log(JSON.stringify({step}));
    assert.ok(step.anklePeak>0.28 && pastLog.z>3.2 && pastLog.y<0.19,'a perna sobe sobre o tronco e o jogador volta ao chão');
    assert.ok(step.flex>0.55 && step.feetDifference>0.10,'joelho flexiona e os pés alternam apoio');
    {
        const contact = await renderer.evaluate(async () => {
            // Mede a geometria do apoio com os dois pés na pose neutra.
            game.resetPlayer();
            const scene = BABYLON.EngineStore.LastCreatedScene;
            const player = scene.getTransformNodeByName('player');
            const log = scene.getMeshByName('place-browser-log');
            const shoes = ['left', 'right'].map(side => scene.getMeshByName('player-shoe-' + side));
            const down = new BABYLON.Vector3(0, -1, 0);
            const samples = [];
            player.rotation.y = 0;
            for (const z of [1.7, 1.8, 1.9, 2, 2.1, 2.2, 2.3]) {
                player.position.set(0, player.position.y, z);
                await new Promise(resolve => scene.onAfterRenderObservable.addOnce(() =>
                    scene.onAfterRenderObservable.addOnce(() => resolve())));
                const feet = shoes.map(shoe => {
                    const { centerWorld, minimumWorld } = shoe.getBoundingInfo().boundingBox;
                    const ray = new BABYLON.Ray(new BABYLON.Vector3(centerWorld.x, 3, centerWorld.z), down);
                    const bark = ray.intersectsMesh(log).pickedPoint;
                    const floor = scene.pickWithRay(ray, mesh => mesh.name.startsWith('ground-')).pickedPoint;
                    return { supportedByLog: !!bark, gap: minimumWorld.y - Math.max(floor.y, bark?.y ?? floor.y) };
                });
                samples.push({ z, feet });
            }
            return samples;
        });
        console.log(JSON.stringify({contact}));
        assert.ok(contact.some(sample => sample.feet.every(foot => foot.supportedByLog)), 'os pés chegam ao topo do tronco');
        assert.ok(contact.every(sample => sample.feet.every(foot => Math.abs(foot.gap)<0.04)),
            'cada sola deve ficar a menos de 4 cm do tronco ou do chão, mesmo nas bordas');
    }
    await apply({kind:'remove',uid:'browser-log'});
    await p.evaluate(()=>game.resetPlayer());
    await apply({...fixture,uid:'browser-high-log',asset:'prop.log',scale:0.95});
    await p.waitForTimeout(T300);
    const highLog=await walk(['w'],850);
    assert.ok(highLog.z>0.5 && highLog.z<1.55 && highLog.y<0.19,'tronco acima do joelho bloqueia');
    await apply({kind:'remove',uid:'browser-high-log'});

    let nativeTree;
    if(main) {
        const tree=await p.evaluate(()=>{
            const s=BABYLON.EngineStore.LastCreatedScene;
            const player=s.getTransformNodeByName('player');
            const tree=s.meshes.filter(m=>m.name.startsWith('tree-tree.pine-trunk-')&&m.isEnabled()&&Math.abs(m.position.x)>6)
                .sort((a,b)=>a.position.lengthSquared()-b.position.lengthSquared())[0];
            if(!tree) return null;
            player.position.set(tree.position.x,tree.position.y+0.25,tree.position.z-1.15);
            return {x:tree.position.x,z:tree.position.z};
        });
        assert.ok(tree,'tronco DLL');
        await p.waitForTimeout(T300);
        nativeTree=await walk(['w'],480);
        assert.ok(Math.abs(nativeTree.x-tree.x)<0.1 && nativeTree.z<tree.z-0.38 && nativeTree.z>tree.z-0.95,'pinheiro do cenário bloqueia '+JSON.stringify({nativeTree,tree}));
    }
    await p.evaluate(()=>game.teleportToGuardian());
    await p.waitForTimeout(500);
    await p.keyboard.press('e');
    await p.locator('.ultima-dialog').waitFor();
    const conversation = await wait(()=>renderer.evaluate(()=>{
        const s=BABYLON.EngineStore.LastCreatedScene;
        const npc=s.getTransformNodeByName('npc');
        const player=s.getTransformNodeByName('player');
        const mouth=s.getMeshByName('npc-mouth');
        const listening=s.getTransformNodeByName('player-head-pivot').rotation.z;
        return mouth.scaling.y>0.016 && listening>0.06 && Math.cos(npc.rotation.y)>0.9 &&
            Math.cos(player.rotation.y)<-0.9;
    }),'os personagens se encaram, a Guardiã fala e o jogador escuta');
    await p.keyboard.press('Escape');
    await p.evaluate(()=>{
        game.addRemote('gesture-test', 'Visitante');
        game.updateRemote('gesture-test', 1, 8, 0);
        game.updateRemote('gesture-test', 1, 10, 0);
    });
    await wait(()=>renderer.evaluate(()=>{
        const s=BABYLON.EngineStore.LastCreatedScene;
        const remote=s.getTransformNodeByName('remote-gesture-test');
        const arm=s.getTransformNodeByName('remote-gesture-test-shoulder-right');
        return remote?.position.z>8 && remote.position.z<9.999 && Math.abs(arm.rotation.x)>0.03;
    }),'o remoto interpola a posição e move os braços');
    const hasRemoteShadow = () => renderer.evaluate(()=>{
        const s=BABYLON.EngineStore.LastCreatedScene;
        const sun=s.getLightByName('sun');
        const casters=(sun.getShadowGenerator(s.activeCamera) ?? sun.getShadowGenerator()).getShadowMap().renderList;
        return casters.some(mesh=>mesh.name.includes('remote-gesture-test'));
    });
    await wait(hasRemoteShadow,'avatar próximo projeta sombra');
    await p.evaluate(()=>game.updateRemote('gesture-test', 40, 8, 0));
    await wait(()=>renderer.evaluate(()=>{
        const s=BABYLON.EngineStore.LastCreatedScene;
        const remote=s.getTransformNodeByName('remote-gesture-test');
        const label=s.getMeshByName('remote-label-gesture-test');
        return remote?.position.x===40 && label?.position.x===40
            && Math.abs(label.position.y-remote.position.y-1.925)<1e-6;
    }),'o teleporte distante mantém o rótulo junto ao avatar');
    await wait(async()=>!(await hasRemoteShadow()),'avatar distante sai do mapa de sombras');
    const behind = await renderer.evaluate(()=>{
        const {activeCamera}=BABYLON.EngineStore.LastCreatedScene;
        const {x,z}=activeCamera.getForwardRay().direction;
        return {x:activeCamera.position.x-x*200,z:activeCamera.position.z-z*200};
    });
    await p.evaluate(({x,z})=>game.updateRemote('gesture-test', x, z, 0),behind);
    const remoteEnabled = enabled => renderer.evaluate(expected=>{
        const s=BABYLON.EngineStore.LastCreatedScene;
        return s.getTransformNodeByName('remote-gesture-test')?.isEnabled()===expected
            && s.getMeshByName('remote-label-gesture-test')?.isEnabled()===expected;
    },enabled);
    await wait(()=>remoteEnabled(false),'avatar e rótulo desativados atrás da câmera');
    await p.evaluate(()=>game.updateRemote('gesture-test', 1, 8, 0));
    await wait(()=>remoteEnabled(true),'avatar e rótulo voltam ao se aproximar');
    await wait(hasRemoteShadow,'avatar que retorna volta a projetar sombra');
    await p.evaluate(()=>game.removeRemote('gesture-test'));
    await wait(()=>renderer.evaluate(()=>{
        const s=BABYLON.EngineStore.LastCreatedScene;
        return !s.getTransformNodeByName('remote-gesture-test') && !s.getMeshByName('remote-label-gesture-test');
    }),'a desconexão remove o avatar e seu rótulo');
    assert.equal(await hasRemoteShadow(),false,'desconexão remove a sombra');
    assert.deepEqual(errors,[]);
    console.log(JSON.stringify({slide,free,soft,stump,rock,highLog,nativeTree,conversation,remoteMotion:true}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
