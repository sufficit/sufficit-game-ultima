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
    async function walkGame(key, budget, axis, target) {
        // Conta o tempo usado pelo motor depois de enviar o comando.
        await p.keyboard.down(key);
        try {
            await renderer.evaluate(({budget,axis,target})=>{
                const scene=BABYLON.EngineStore.LastCreatedScene;
                const player=scene.getTransformNodeByName('player');
                globalThis.contactTraversal=new Promise((resolve,reject)=>{
                    let elapsed=0;
                    const timer=setTimeout(()=>{
                        scene.onAfterRenderObservable.remove(observe);
                        reject(Error('tempo limite no percurso de contato'));
                    },15000);
                    const observe=scene.onAfterRenderObservable.add(()=>{
                        elapsed+=Math.min(0.05,scene.getEngine().getDeltaTime()/1000);
                        if(player.position[axis]<=target && elapsed<budget) return;
                        scene.onAfterRenderObservable.remove(observe);
                        clearTimeout(timer);
                        const {x,y,z}=player.position;
                        resolve({x,y,z,yaw:player.rotation.y,elapsed});
                    });
                });
            },{budget,axis,target});
            return await renderer.evaluate(()=>globalThis.contactTraversal);
        } finally { await p.keyboard.up(key); await p.waitForTimeout(T300); }
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
            const knees=['left','right'].map(side=>scene.getTransformNodeByName('player-knee-'+side));
            const observer=scene.onAfterRenderObservable.add(() => {
                const feet=ankles.map((ankle,i)=>{
                    const knee=knees[i], hip=knee.parent;
                    const upper=knee.getAbsolutePosition().subtract(hip.getAbsolutePosition());
                    const lower=ankle.getAbsolutePosition().subtract(knee.getAbsolutePosition());
                    const bend=upper.subtract(lower).scaleInPlace(0.5);
                    const lateral=bend.dot(hip.parent.getDirection(BABYLON.Axis.X));
                    const kneeSwivel=bend.length()>0.025 ? lateral/bend.length() : 0;
                    const direction=ankle.getDirection(BABYLON.Axis.Z);
                    const shoe=scene.getMeshByName(ankle.name.replace('ankle','shoe'));
                    const {x,z}=shoe.getAbsolutePosition();
                    const pitch=Math.asin(Math.max(-1,Math.min(1,-direction.y)));
                    return {x,z,pitch,kneeSwivel,toeSide: direction.x*Math.cos(player.rotation.y)-direction.z*Math.sin(player.rotation.y),planted:Math.abs(pitch)<1e-6};
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
        let g=0, gC=0, dist=0, n=0, maxSideSpeed=0, maxBackSpeed=0, plantedFrames=0, maxSlide=0, turningFrames=0, turnSlide=0, swingSamples=0, swingRetreat=0, swingRetreatAfterTakeoff=0;
        const swingOrigins=[null,null];
        for(let i=1;i<samples.length;i++){
            const d=Math.hypot(samples[i].x-samples[i-1].x, samples[i].z-samples[i-1].z);
            if(g>0.3){
                dist+=d; gC+=samples[i].g; n++;
                const yaw=samples[i].yaw;
                const side=(samples[i].x-samples[i-1].x)*Math.cos(yaw) -
                    (samples[i].z-samples[i-1].z)*Math.sin(yaw);
                const forward=(samples[i].x-samples[i-1].x)*Math.sin(yaw) +
                    (samples[i].z-samples[i-1].z)*Math.cos(yaw);
                if(samples[i].g>0){
                    maxSideSpeed=Math.max(maxSideSpeed,Math.abs(side)/samples[i].g);
                    maxBackSpeed=Math.max(maxBackSpeed,-forward/samples[i].g);
                }
                for(let foot=0;foot<2;foot++){
                    const a=samples[i-1].feet[foot], b=samples[i].feet[foot];
                    if(b.pitch<=1e-6 || Math.abs(yaw-samples[i-1].yaw)>=0.001) swingOrigins[foot]=null;
                    else if(a.planted) swingOrigins[foot]={sample:samples[i-1], allowance:d};
                    const origin=swingOrigins[foot];
                    if(origin && d>0.02){
                        const forwardX=Math.sin(yaw), forwardZ=Math.cos(yaw);
                        const retreat=((samples[i].x-origin.sample.x)-(b.x-origin.sample.feet[foot].x))*forwardX +
                            ((samples[i].z-origin.sample.z)-(b.z-origin.sample.feet[foot].z))*forwardZ;
                        swingRetreat=Math.max(swingRetreat,retreat);
                        // A saída do apoio pode ocorrer em qualquer instante entre duas amostras.
                        swingRetreatAfterTakeoff=Math.max(swingRetreatAfterTakeoff,retreat-origin.allowance);
                        swingSamples++;
                    }
                    if(a.planted && b.planted && d<0.2){
                        const slide=Math.hypot(b.x-a.x,b.z-a.z);
                        if(d>0.02){
                            plantedFrames++;
                            maxSlide=Math.max(maxSlide,slide);
                        }
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
        const swivels=samples.flatMap(sample=>sample.feet.filter(foot=>Math.abs(foot.toeSide)>0.45)
            .map(foot=>foot.kneeSwivel*Math.sign(foot.toeSide)));
        const kneeAlignment=Math.max(0,...swivels);
        const closestGap=Math.min(...samples.map(({yaw,feet:[left,right]})=>
            (right.x-left.x)*Math.cos(yaw)-(right.z-left.z)*Math.sin(yaw)));
        return {v:gC>0?dist/gC:0,x:last?.x,z:last?.z,frames:samples.length,plantedFrames,maxSlide,
            minPitch:Math.min(...pitches),maxPitch:Math.max(...pitches),turningFrames,turnSlide,stopFrames,stopSlide,swingSamples,swingRetreat,swingRetreatAfterTakeoff,maxSideSpeed,maxBackSpeed,closestGap,kneeAlignment};
    }
    await p.evaluate(()=>{
        game.addRemote('pose-arm','Contato em repouso');
        game.updateRemote('pose-arm',0.63,0,0);
    });
    await wait(()=>renderer.evaluate(()=>{
        const scene=BABYLON.EngineStore.LastCreatedScene;
        return scene.getTransformNodeByName('remote-pose-arm')?.position.x===0.63 &&
            !!scene.getMeshByName('char-character.wanderer-arm-left-remote-pose-arm');
    }),'avatar em repouso pronto');
    const forcedOverlap=await renderer.evaluate(()=>{
        const scene=BABYLON.EngineStore.LastCreatedScene;
        scene.getTransformNodeByName('player-shoulder-right').rotation.z=0.8;
        scene.getTransformNodeByName('remote-pose-arm-shoulder-left').rotation.z=-0.8;
        const a=scene.getMeshByName('char-character.villager-arm-right-player');
        const b=scene.getMeshByName('char-character.wanderer-arm-left-remote-pose-arm');
        a.computeWorldMatrix(true);
        b.computeWorldMatrix(true);
        return a.intersectsMesh(b,true);
    });
    assert.ok(forcedOverlap,'a pose de teste produz interseção dos braços');
    const armGap=await wait(()=>renderer.evaluate(()=>{
        const scene=BABYLON.EngineStore.LastCreatedScene;
        const a=scene.getMeshByName('char-character.villager-arm-right-player');
        const b=scene.getMeshByName('char-character.wanderer-arm-left-remote-pose-arm');
        const player=scene.getTransformNodeByName('player').position;
        const other=scene.getTransformNodeByName('remote-pose-arm').position;
        const gap=Math.hypot(other.x-player.x,other.z-player.z);
        return !a.intersectsMesh(b,true) && Math.hypot(player.x,player.z)<0.005 && Math.abs(gap-0.63)<0.005 ? gap : 0;
    }),'articulações separam os braços sem mover os dois personagens');
    const settled=await renderer.evaluate(async ()=>{
        const scene=BABYLON.EngineStore.LastCreatedScene;
        const a=scene.getMeshByName('char-character.villager-arm-right-player');
        const b=scene.getMeshByName('char-character.wanderer-arm-left-remote-pose-arm');
        const shoulderA=scene.getTransformNodeByName('player-shoulder-right');
        const shoulderB=scene.getTransformNodeByName('remote-pose-arm-shoulder-left');
        const player=scene.getTransformNodeByName('player').position;
        const remote=scene.getTransformNodeByName('remote-pose-arm').position;
        let bent=false;
        for(let frame=0;frame<8;frame++){
            shoulderA.rotation.z=0.8;
            shoulderB.rotation.z=-0.8;
            await new Promise(resolve=>scene.onAfterRenderObservable.addOnce(()=>resolve()));
            if(a.intersectsMesh(b,true) || Math.hypot(player.x,player.z)>=0.005 ||
                Math.hypot(remote.x-0.63,remote.z)>=0.005)
                return {frame, overlap:a.intersectsMesh(b,true), player:[player.x,player.z], remote:[remote.x,remote.z]};
            bent ||= Math.abs(scene.getTransformNodeByName('player-elbow-right').rotation.x)>0.1 ||
                Math.abs(scene.getTransformNodeByName('remote-pose-arm-elbow-left').rotation.x)>0.1;
        }
        return {bent};
    });
    assert.ok(settled.bent,'os braços continuam separados nos quadros seguintes: '+JSON.stringify(settled));
    console.log(JSON.stringify({armGap}));
    await renderer.evaluate(()=>{
        BABYLON.EngineStore.LastCreatedScene.getTransformNodeByName('player-shoulder-right').rotation.z=0.055;
    });
    await p.evaluate(()=>game.removeRemote('pose-arm'));
    await p.waitForTimeout(T300);
    await p.evaluate(()=>{
        game.addRemote('contact-a', 'Contato A');
        game.updateRemote('contact-a', 0, 3, 0);
        game.addRemote('contact-b', 'Contato B');
        game.updateRemote('contact-b', 0, 3, 0);
    });
    const avatarGap=await wait(()=>renderer.evaluate(()=>{
        const scene=BABYLON.EngineStore.LastCreatedScene;
        const a=scene.getTransformNodeByName('remote-contact-a');
        const b=scene.getTransformNodeByName('remote-contact-b');
        if(Math.abs((a?.position.z ?? -10)-3)>0.65 || Math.abs((b?.position.z ?? -10)-3)>0.65) return 0;
        const gap=Math.hypot(a.position.x-b.position.x,a.position.z-b.position.z);
        return gap>=0.33 && gap<=0.69 ? gap : 0;
    }), 'duas poses sobrepostas se separam perto da silhueta');
    await p.evaluate(()=>game.removeRemote('contact-b'));
    await renderer.evaluate(()=>{
        const scene=BABYLON.EngineStore.LastCreatedScene;
        const player=scene.getTransformNodeByName('player');
        const torso=scene.getTransformNodeByName('player-upper-body');
        const actor=scene.getTransformNodeByName('remote-contact-a');
        globalThis.bodyImpact=new Promise((resolve,reject)=>{
            let elapsed=0, maxZ=0, minBend=0;
            const timer=setTimeout(()=>{
                scene.onAfterRenderObservable.remove(observe);
                reject(Error('choque não observado: '+JSON.stringify({maxZ,minBend,elapsed})));
            },12000);
            const observe=scene.onAfterRenderObservable.add(()=>{
                elapsed+=Math.min(0.05,scene.getEngine().getDeltaTime()/1000);
                const z=player.position.z, bend=torso.rotation.x;
                maxZ=Math.max(maxZ,z);
                minBend=Math.min(minBend,bend);
                if(z<2.32 || z>2.85 || bend>=-0.16) return;
                const front=Math.max(...player.getChildMeshes().map(mesh=>mesh.getBoundingInfo().boundingBox.maximumWorld.z));
                const back=Math.min(...actor.getChildMeshes().map(mesh=>mesh.getBoundingInfo().boundingBox.minimumWorld.z));
                if(back-front>=0.1) return;
                scene.onAfterRenderObservable.remove(observe);
                clearTimeout(timer);
                resolve({z,bend,gap:back-front});
            });
        });
    });
    await p.keyboard.down('Shift');
    await p.keyboard.down('w');
    try {
        const stumble=await renderer.evaluate(()=>globalThis.bodyImpact);
        console.log(JSON.stringify({avatarGap,stumble}));
    } finally {
        await p.keyboard.up('w');
        await p.keyboard.up('Shift');
    }
    const beside=await walkGame('d',1.3,'x',0.85);
    const around=await walkGame('w',1.1,'z',3.7);
    assert.ok(beside.x>0.8 && around.z>3.7,'é possível contornar os personagens: '+JSON.stringify({beside,around}));
    await p.evaluate(()=>{
        game.removeRemote('contact-a');
        game.resetPlayer();
    });
    await p.waitForTimeout(T300);
    await p.evaluate(()=>{
        game.addRemote('contact-side','Contato lateral');
        game.updateRemote('contact-side',1.8,0,0);
    });
    await wait(()=>renderer.evaluate(()=>BABYLON.EngineStore.LastCreatedScene
        .getTransformNodeByName('remote-contact-side')?.position.x===1.8),'personagem lateral pronto');
    const sideContact=await walk(['d'],900,false);
    const sideGap=await renderer.evaluate(()=>{
        const scene=BABYLON.EngineStore.LastCreatedScene;
        const meshes=name=>scene.getTransformNodeByName(name).getChildMeshes()
            .filter(mesh=>mesh.isEnabled() && mesh.isVisible);
        const front=Math.max(...meshes('player').map(mesh=>mesh.getBoundingInfo().boundingBox.maximumWorld.x));
        const back=Math.min(...meshes('remote-contact-side').map(mesh=>mesh.getBoundingInfo().boundingBox.minimumWorld.x));
        return back-front;
    });
    assert.ok(sideContact.x>1 && sideContact.x<1.66 && sideGap<0.14,
        'chega junto ao corpo de lado: '+JSON.stringify({sideContact,sideGap}));
    await p.evaluate(()=>{ game.removeRemote('contact-side'); game.resetPlayer(); });
    await p.waitForTimeout(T300);
    await p.evaluate(()=>{
        game.addRemote('contact-gap-left','Passagem esquerda');
        game.updateRemote('contact-gap-left',-0.60,2.65,0);
        game.addRemote('contact-gap-right','Passagem direita');
        game.updateRemote('contact-gap-right',0.60,2.65,0);
    });
    await wait(()=>renderer.evaluate(()=>{
        const s=BABYLON.EngineStore.LastCreatedScene;
        return s.getTransformNodeByName('remote-contact-gap-left')?.position.z===2.65 &&
            s.getTransformNodeByName('remote-contact-gap-right')?.position.z===2.65;
    }),'passagem entre personagens pronta');
    const throughGap=await walkGame('w',1.37,'z',3.45);
    assert.ok(throughGap.z>3.45 && Math.abs(throughGap.x)<0.15,
        'passa entre dois corpos com espaço livre: '+JSON.stringify(throughGap));
    console.log(JSON.stringify({sideContact,sideGap,beside,around,throughGap}));
    await p.evaluate(()=>{
        game.removeRemote('contact-gap-left');
        game.removeRemote('contact-gap-right');
        game.resetPlayer();
    });
    await p.waitForTimeout(T300);
    const walking=await speed(['w'],1800,false);
    assert.ok(walking.plantedFrames>=5 && walking.maxSlide<0.012,
        'pé de apoio deve ficar fixo no mundo durante a marcha em linha reta: '+JSON.stringify(walking));
    assert.ok(walking.swingSamples>=3 && walking.swingRetreatAfterTakeoff<0.025,
        'pé levantado deve avançar na passada: '+JSON.stringify(walking));
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
    assert.ok(turning.maxSideSpeed<1.1 && turning.v>2,
        'corpo deve virar antes de acelerar lateralmente: '+JSON.stringify(turning));
    assert.ok(turning.turningFrames>=2 && turning.turnSlide<0.012,
        'pé de apoio deve ficar no mesmo lugar durante a curva: '+JSON.stringify(turning));
    await p.evaluate(()=>game.resetPlayer());
    await p.waitForTimeout(T300);
    const reversing=await speed(['w'],1500,false,'s');
    console.log(JSON.stringify({reversing}));
    assert.ok(reversing.kneeAlignment>0.2,
        'o plano de flexão deve acompanhar a sola na inversão: '+JSON.stringify(reversing));
    assert.ok(reversing.closestGap>=0.14,'os sapatos não devem cruzar sob o corpo: '+JSON.stringify(reversing));
    assert.ok(reversing.maxBackSpeed<0.1,'jogador deve girar antes de seguir na direção oposta');
    assert.ok(reversing.turningFrames>=2 && reversing.turnSlide<0.012,
        'pé de apoio deve ficar no mesmo lugar durante a inversão: '+JSON.stringify(reversing));
    assert.ok(reversing.stopFrames>=2 && reversing.stopSlide<0.012,
        'pé em apoio deve permanecer imóvel ao parar: '+JSON.stringify(reversing));
    await p.evaluate(()=>game.resetPlayer());
    await p.waitForTimeout(T300);
    const diagonal=await speed(['w','d'],1600,false);
    console.log(JSON.stringify({diagonal}));
    assert.ok(diagonal.v>3.3 && diagonal.v<3.7 && diagonal.x>1 && Math.abs(diagonal.x-diagonal.z)<0.08,
        'diagonal deve manter o rumo e a velocidade de caminhada: '+JSON.stringify(diagonal));
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
