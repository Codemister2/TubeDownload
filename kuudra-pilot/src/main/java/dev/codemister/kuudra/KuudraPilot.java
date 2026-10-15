package dev.codemister.kuudra;

import com.mojang.blaze3d.platform.InputConstants;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.command.v2.ClientCommandRegistrationCallback;
import net.fabricmc.fabric.api.client.command.v2.ClientCommands;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.keymapping.v1.KeyMappingHelper;
import net.fabricmc.fabric.api.client.message.v1.ClientReceiveMessageEvents;
import net.fabricmc.fabric.api.client.rendering.v1.hud.HudElementRegistry;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.Minecraft;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;

import java.lang.reflect.Method;
import java.nio.file.*;
import java.io.*;
import java.util.*;

public final class KuudraPilot implements ClientModInitializer {
    static final String ID = "kuudrapilot";
    static final Config cfg = new Config();
    static final Bot bot = new Bot();
    private KeyMapping menuKey, toggleKey, stopKey;
    private Object lastLevel;
    static int tab = 0;

    @Override public void onInitializeClient() {
        cfg.load();
        KeyMapping.Category group = KeyMapping.Category.register(Identifier.fromNamespaceAndPath(ID, "controls"));
        menuKey = KeyMappingHelper.registerKeyMapping(new KeyMapping("key.kuudrapilot.menu",
                InputConstants.Type.KEYSYM, InputConstants.KEY_RSHIFT, group));
        toggleKey = KeyMappingHelper.registerKeyMapping(new KeyMapping("key.kuudrapilot.toggle",
                InputConstants.Type.KEYSYM, InputConstants.KEY_F9, group));
        stopKey = KeyMappingHelper.registerKeyMapping(new KeyMapping("key.kuudrapilot.stop",
                InputConstants.Type.KEYSYM, InputConstants.KEY_F8, group));

        ClientCommandRegistrationCallback.EVENT.register((dispatcher, registryAccess) ->
                dispatcher.register(ClientCommands.literal("kp")
                    .executes(c -> command("gui"))
                    .then(ClientCommands.literal("gui").executes(c -> command("gui")))
                    .then(ClientCommands.literal("status").executes(c -> command("status")))
                    .then(ClientCommands.literal("arm").executes(c -> command("arm")))
                    .then(ClientCommands.literal("start").executes(c -> command("start")))
                    .then(ClientCommands.literal("stop").executes(c -> command("stop")))
                    .then(ClientCommands.literal("center").executes(c -> command("center")))
                    .then(ClientCommands.literal("lock").executes(c -> command("lock")))
                    .then(ClientCommands.literal("picked").executes(c -> command("picked")))
                    .then(ClientCommands.literal("delivered").executes(c -> command("delivered")))
                    .then(ClientCommands.literal("scan").executes(c -> command("scan")))
                    .then(ClientCommands.literal("help").executes(c -> command("help")))
                ));

        ClientReceiveMessageEvents.GAME.register((message, overlay) -> {
            if (message != null) bot.readChat(message.getString());
        });
        ClientTickEvents.END_CLIENT_TICK.register(this::tick);
        HudElementRegistry.addLast(Identifier.fromNamespaceAndPath(ID, "hud"), (draw, delta) -> {
            Minecraft mc = Minecraft.getInstance();
            if (!cfg.overlay || mc.player == null || mc.screen instanceof UI) return;
            draw.fill(8, 8, 264, 65, 0xDC111821);
            draw.fill(8, 8, 11, 65, bot.running ? 0xFF45BBA9 : 0xFF808FA8);
            draw.text(mc.font, "KUUDRA PILOT  |  " + (bot.running ? "ACTIVE" : "IDLE"), 18, 16, 0xFFE5EAF0, false);
            draw.text(mc.font, bot.state.name() + "    |    delivered " + bot.delivered + "    pulls " + bot.pulls,
                    18, 32, 0xFF9CAEC5, false);
            draw.text(mc.font, shorten(bot.detail, 37), 18, 47, 0xFF81D8C8, false);
        });
        System.out.println("[KuudraPilot] Fabric 26.1.2 initialized, commands /kp registered");
    }

    private int command(String sub) {
        Minecraft m = Minecraft.getInstance();
        switch (sub) {
            case "gui" -> m.setScreen(new UI(m.screen));
            case "help" -> tell("/kp gui, arm, center, start, stop, scan, lock, picked, delivered, status");
            case "status" -> tell(bot.describe());
            case "arm" -> bot.arm(m);
            case "center" -> bot.center(m);
            case "start" -> bot.start(m);
            case "stop" -> bot.stop("Manual stop");
            case "lock" -> bot.lock(m);
            case "picked" -> bot.picked();
            case "delivered" -> bot.delivered();
            case "scan" -> {
                Target t = Bot.findSupply(m);
                tell(t == null ? "No named supply visible. Aim at crate then /kp lock" : "Supply: " + t.label);
            }
        }
        return 1;
    }

    private void tick(Minecraft m) {
        if (m.level != lastLevel) {
            if (lastLevel != null) bot.reset("World changed");
            lastLevel = m.level;
        }
        try {
            while (stopKey.consumeClick()) bot.stop("F8 emergency stop");
            while (menuKey.consumeClick()) m.setScreen(new UI(m.screen));
            while (toggleKey.consumeClick()) {
                if (bot.running) bot.stop("F9");
                else bot.start(m);
            }
            bot.tick(m);
        } catch (Throwable t) {
            System.err.println("[KuudraPilot] Tick safety stop: " + t);
            bot.stop("Unexpected mod error; check logs");
        }
    }

    static void tell(String msg) {
        Minecraft m = Minecraft.getInstance();
        String text = "[KuudraPilot] " + msg;
        if (m.player != null) m.player.sendSystemMessage(Component.literal(text));
        else System.out.println(text);
    }

    static String shorten(String s, int max) {
        return s.length() <= max ? s : s.substring(0, Math.max(0, max-3)) + "...";
    }

    enum State { IDLE, FIND, AIM, CAST, WAIT, REEL, RECOVER, PICKUP, CARRY, PEARL, LAND, DELIVERY, PAUSED }
    record Target(double x, double y, double z, String label) { }

    static final class Bot {
        State state = State.IDLE;
        boolean running, armed, hasSupply, threwPearl;
        Object armedLevel;
        Target target;
        String detail = "Ready";
        long enteredAt, lastUse, lastScan, runStart;
        int pulls, casts, delivered, failures;

        void phase(State s, String desc) { state = s; detail = desc; enteredAt = System.currentTimeMillis(); }
        void reset(String reason) {
            running = armed = hasSupply = threwPearl = false;
            armedLevel = null; target = null;
            phase(State.IDLE, reason);
        }
        void stop(String reason) {
            running = false;
            phase(State.IDLE, reason);
            tell(reason);
        }
        void pause(String reason) {
            running = false;
            failures++;
            phase(State.PAUSED, reason);
            tell("PAUSED: " + reason);
        }
        void arm(Minecraft m) {
            if (m.level == null || m.player == null) { tell("Enter Kuudra first."); return; }
            armed = true; armedLevel = m.level; running = false; hasSupply = false;
            phase(State.IDLE, "Armed; use /kp center and /kp start");
            tell("Armed in this world. F8 is emergency stop.");
        }
        void center(Minecraft m) {
            if (m.player == null) { tell("Join a world first."); return; }
            cfg.centerX = m.player.getX(); cfg.centerY = m.player.getY(); cfg.centerZ = m.player.getZ();
            cfg.centerSaved = true; cfg.save();
            tell(String.format(Locale.US, "Center %.1f / %.1f / %.1f saved", cfg.centerX, cfg.centerY, cfg.centerZ));
        }
        void start(Minecraft m) {
            if (running) { tell("Already running"); return; }
            if (m.player == null || m.level == null || !armed || armedLevel != m.level) {
                tell("Use /kp arm in the current Kuudra world first."); return;
            }
            if (!cfg.centerSaved) { tell("Set center first with /kp center"); return; }
            if (cfg.fish && !hotbarHas(m, cfg.rod, "rod")) {
                tell("Lava rod missing in configured slot " + cfg.rod); return;
            }
            if (cfg.pearl && !hotbarHas(m, cfg.pearlSlot, "pearl")) {
                tell("Ender Pearl missing in slot " + cfg.pearlSlot); return;
            }
            target = null; hasSupply = threwPearl = false;
            casts = pulls = delivered = failures = 0;
            running = true; runStart = System.currentTimeMillis(); lastUse = lastScan = 0;
            phase(State.FIND, "Finding supplies");
            tell("Started - fish " + (cfg.fish ? "AUTO" : "MANUAL") + "; pearl " + (cfg.pearl ? "AUTO" : "MANUAL"));
        }
        void lock(Minecraft m) {
            if (m.hitResult == null || m.hitResult.getType() == HitResult.Type.MISS) {
                tell("Look at supply or lava near the crate first."); return;
            }
            Vec3 v = m.hitResult.getLocation();
            target = new Target(v.x, v.y, v.z, "Crosshair");
            if (running && (state == State.FIND || state == State.AIM)) phase(State.AIM, "Crosshair target selected");
            tell("Crosshair target locked");
        }
        void picked() {
            if (!running) { tell("Not running"); return; }
            if (hasSupply) return;
            hasSupply = true; phase(State.CARRY, "Supply picked up; bring to middle"); tell("Pickup confirmed.");
        }
        void delivered() {
            if (!running) { tell("Not running"); return; }
            if (!hasSupply) { tell("No supply carried; use /kp picked after pickup"); return; }
            delivered++; hasSupply = threwPearl = false; target = null; pulls = 0;
            phase(State.FIND, "Delivered #" + delivered);
            tell("Supply delivered. Finding next.");
        }
        void readChat(String input) {
            if (!running || input == null) return;
            String m = input.replaceAll("(?i)§[0-9a-fk-or]", "").toLowerCase(Locale.ROOT);
            if (m.contains("suppl") && (m.contains("picked up") || m.contains("collected") || m.contains("recovered"))) picked();
            else if (m.contains("suppl") && (m.contains("delivered") || m.contains("deposited"))) delivered();
            else if (m.contains("suppl") && m.contains("can't move any further")) pause("Crate blocked; shift fishing position manually");
        }
        void tick(Minecraft m) {
            if (!running) return;
            if (m.level == null || m.player == null || m.level != armedLevel) {
                reset("Left Kuudra instance"); return;
            }
            if (m.screen != null) return;
            if (m.player.getHealth() <= cfg.minHealth) { pause("Health safety threshold"); return; }
            if (m.player.isInLava()) { pause("Player in lava"); return; }
            long now = System.currentTimeMillis();
            if (state == State.FIND) {
                if (target == null && now - lastScan > 450) {
                    lastScan = now; target = findSupply(m);
                }
                if (target != null) phase(State.AIM, "Target: " + target.label);
                else detail = "No supply entity; use /kp lock";
            } else if (state == State.AIM) {
                if (target == null) { phase(State.FIND, "Rescan"); return; }
                double d = distance(m, target);
                if (d < 3.6) { phase(State.PICKUP, "Stay still for pickup meter"); return; }
                if (!cfg.fish) { detail = "Fish manually; /kp picked when collected"; return; }
                if (!hotbarHas(m, cfg.rod, "rod")) { pause("Lava rod missing"); return; }
                if (aim(m, target.x, target.y + 0.25, target.z, cfg.smooth ? 4.0f : 180f))
                    phase(State.CAST, "Casting at supply");
            } else if (state == State.CAST) {
                if (now - lastUse < 600) return;
                if (!use(m, cfg.rod)) { pause("Could not use lava rod"); return; }
                casts++; lastUse = now; phase(State.WAIT, "Hook in flight");
            } else if (state == State.WAIT) {
                if (now - enteredAt >= cfg.reelDelay) phase(State.REEL, "Reeling supply");
            } else if (state == State.REEL) {
                if (now - lastUse < 500) return;
                if (!use(m, cfg.rod)) { pause("Failed reeling lava rod"); return; }
                pulls++; lastUse = now; phase(State.RECOVER, "Checking supply distance");
            } else if (state == State.RECOVER) {
                if (now - enteredAt < 850) return;
                if (target != null && distance(m,target) < 3.6) {
                    phase(State.PICKUP, "Hold position for pickup"); return;
                }
                if (pulls >= cfg.maxPulls) { pause("Supply not recovered after " + pulls + " pulls; aim at crate and retry"); return; }
                Target next = findSupply(m);
                if (next != null) target = next;
                phase(State.AIM, "Re-aiming supply");
            } else if (state == State.PICKUP) {
                if (now-enteredAt > 17000) pause("Pickup unconfirmed. Check pickup meter then /kp picked");
            } else if (state == State.CARRY) {
                if (centerDist(m) < 3.4) { phase(State.DELIVERY, "At center: wait for delivery"); return; }
                if (!cfg.pearl) { detail = "Carry supplies to middle, then /kp delivered"; return; }
                if (threwPearl) return;
                if (!hotbarHas(m, cfg.pearlSlot, "pearl")) { pause("No Ender Pearl"); return; }
                double d = centerDist(m);
                if (d < 4 || d > 42) { pause("Pearl outside calibrated 4-42 block range"); return; }
                phase(State.PEARL, "Aim for saved center");
            } else if (state == State.PEARL) {
                double dx=cfg.centerX-m.player.getX(), dz=cfg.centerZ-m.player.getZ();
                double horizontal=Math.hypot(dx,dz), vertical=cfg.centerY-m.player.getY();
                if (horizontal <= 0) { phase(State.DELIVERY, "At center"); return; }
                float yaw=(float)Math.toDegrees(Math.atan2(-dx,dz));
                float pitch=(float)(-21.0 - 0.27*horizontal + Math.toDegrees(Math.atan2(vertical,horizontal))*0.75);
                pitch=Math.max(-62,Math.min(-10,pitch));
                if (!aimAngles(m,yaw,pitch,cfg.smooth ? 4f : 180f)) return;
                if (now-lastUse<650) return;
                if (!use(m,cfg.pearlSlot)) { pause("Could not throw Ender Pearl"); return; }
                threwPearl=true; lastUse=now; phase(State.LAND, "Pearl thrown once; waiting to land");
            } else if (state == State.LAND) {
                if (now-enteredAt>550 && centerDist(m)<3.4) phase(State.DELIVERY, "At center; wait for delivery");
                else if (now-enteredAt>9500) pause("Pearl missed center; stopped for manual delivery");
            } else if (state == State.DELIVERY && now-enteredAt>20000) {
                pause("Delivery not detected; /kp delivered if actually placed");
            }
        }
        String describe() { return (running?"RUNNING":"STOPPED")+"/"+state+" - "+detail+"; pulls "+pulls+", delivered "+delivered; }
        double centerDist(Minecraft m) {
            if (m.player==null) return 999;
            return Math.hypot(cfg.centerX-m.player.getX(),cfg.centerZ-m.player.getZ());
        }
        static double distance(Minecraft m, Target t) {
            double dx=m.player.getX()-t.x,dy=m.player.getY()-t.y,dz=m.player.getZ()-t.z;
            return Math.sqrt(dx*dx+dy*dy+dz*dz);
        }
        static boolean hotbarHas(Minecraft m, int slot, String word) {
            if (m.player == null || slot < 1 || slot > 9) return false;
            var stack = m.player.getInventory().getItem(slot-1);
            return !stack.isEmpty() && stack.getHoverName().getString().toLowerCase(Locale.ROOT).contains(word);
        }
        static boolean select(Minecraft m, int slot) {
            if (m.player==null || slot<1 || slot>9) return false;
            try {
                // Reflection here avoids a hard crash when hotbar selection signatures change.
                Object inv=m.player.getInventory();
                Method set=inv.getClass().getMethod("setSelectedSlot",int.class);
                set.invoke(inv,slot-1); return true;
            } catch (ReflectiveOperationException e) {
                tell("Hotbar selection incompatible with current client: " + e.getClass().getSimpleName());
                return false;
            }
        }
        static boolean use(Minecraft m, int slot) {
            if (m.gameMode==null || m.player==null || !select(m,slot)) return false;
            m.gameMode.useItem(m.player,InteractionHand.MAIN_HAND);
            return true;
        }
        static boolean aim(Minecraft m,double x,double y,double z,float step) {
            if(m.player==null)return false;
            double dx=x-m.player.getX(), dz=z-m.player.getZ();
            double dy=y-(m.player.getY()+m.player.getEyeHeight());
            float yaw=(float)Math.toDegrees(Math.atan2(-dx,dz));
            float pitch=(float)-Math.toDegrees(Math.atan2(dy,Math.hypot(dx,dz)));
            return aimAngles(m,yaw,pitch,step);
        }
        static boolean aimAngles(Minecraft m,float yaw,float pitch,float step) {
            if(m.player==null)return false;
            float oldYaw=m.player.getYRot(),oldPitch=m.player.getXRot();
            float changeYaw=((yaw-oldYaw+540f)%360f)-180f;
            float changePitch=pitch-oldPitch;
            m.player.setYRot(oldYaw+Math.max(-step,Math.min(step,changeYaw)));
            m.player.setXRot(Math.max(-90,Math.min(90,oldPitch+Math.max(-step,Math.min(step,changePitch)))));
            return Math.abs(changeYaw)<1.8f && Math.abs(changePitch)<1.8f;
        }
        static Target findSupply(Minecraft m) {
            if (m.level==null || m.player==null) return null;
            try {
                Method get=m.level.getClass().getMethod("entitiesForRendering");
                Object list=get.invoke(m.level);
                if (!(list instanceof Iterable<?> iter)) return null;
                Target chosen=null;double best=75*75;
                for (Object entity : iter) {
                    try {
                        Object name=entity.getClass().getMethod("getName").invoke(entity);
                        String label=(String)name.getClass().getMethod("getString").invoke(name);
                        String l=label.toLowerCase(Locale.ROOT);
                        if (!l.contains("suppl") && !l.contains("crate")) continue;
                        double x=((Number)entity.getClass().getMethod("getX").invoke(entity)).doubleValue();
                        double y=((Number)entity.getClass().getMethod("getY").invoke(entity)).doubleValue();
                        double z=((Number)entity.getClass().getMethod("getZ").invoke(entity)).doubleValue();
                        double d=(m.player.getX()-x)*(m.player.getX()-x)
                                +(m.player.getY()-y)*(m.player.getY()-y)
                                +(m.player.getZ()-z)*(m.player.getZ()-z);
                        if (d<best) { best=d;chosen=new Target(x,y,z,label); }
                    } catch (ReflectiveOperationException ignored) { }
                }
                return chosen;
            } catch (ReflectiveOperationException ignored) { return null; }
        }
    }

    static final class Config {
        boolean overlay=true, fish=false, pearl=false, smooth=true, centerSaved=false;
        int rod=1,pearlSlot=2,maxPulls=6,minHealth=6,reelDelay=1550;
        double centerX,centerY,centerZ;
        private Path path() {
            return FabricLoader.getInstance().getConfigDir().resolve("kuudrapilot.properties");
        }
        void load() {
            Properties p=new Properties();
            try (InputStream in=Files.newInputStream(path())) { p.load(in); }
            catch (IOException ignored) { return; }
            overlay=Boolean.parseBoolean(p.getProperty("overlay","true"));
            fish=Boolean.parseBoolean(p.getProperty("fish","false"));
            pearl=Boolean.parseBoolean(p.getProperty("pearl","false"));
            smooth=Boolean.parseBoolean(p.getProperty("smooth","true"));
            centerSaved=Boolean.parseBoolean(p.getProperty("centerSaved","false"));
            rod=num(p,"rod",1,1,9); pearlSlot=num(p,"pearlSlot",2,1,9);
            maxPulls=num(p,"maxPulls",6,1,14);
            minHealth=num(p,"minHealth",6,1,20);
            reelDelay=num(p,"reelDelay",1550,800,3800);
            centerX=dbl(p,"cx",0); centerY=dbl(p,"cy",0); centerZ=dbl(p,"cz",0);
        }
        void save() {
            Properties p=new Properties();
            p.setProperty("overlay",String.valueOf(overlay));p.setProperty("fish",String.valueOf(fish));
            p.setProperty("pearl",String.valueOf(pearl));p.setProperty("smooth",String.valueOf(smooth));
            p.setProperty("centerSaved",String.valueOf(centerSaved));
            p.setProperty("rod",String.valueOf(rod));p.setProperty("pearlSlot",String.valueOf(pearlSlot));
            p.setProperty("maxPulls",String.valueOf(maxPulls));
            p.setProperty("minHealth",String.valueOf(minHealth));
            p.setProperty("reelDelay",String.valueOf(reelDelay));
            p.setProperty("cx",String.valueOf(centerX));p.setProperty("cy",String.valueOf(centerY));
            p.setProperty("cz",String.valueOf(centerZ));
            try { Files.createDirectories(path().getParent());
                try(OutputStream out=Files.newOutputStream(path())) { p.store(out,"Kuudra Pilot"); }
            } catch(IOException e) { System.err.println("[KuudraPilot] Cannot save config "+e); }
        }
        static int num(Properties p,String k,int v,int min,int max) {
            try {return Math.min(max,Math.max(min,Integer.parseInt(p.getProperty(k,""+v))));}
            catch(NumberFormatException e){return v;}
        }
        static double dbl(Properties p,String k,double d) {
            try {return Double.parseDouble(p.getProperty(k,""+d));}
            catch(NumberFormatException e){return d;}
        }
    }

    public static final class UI extends Screen {
        final Screen previous;
        int left,top,w,h,page;
        UI(Screen prev){ super(Component.literal("Kuudra Pilot"));previous=prev;page=tab; }
        @Override protected void init() {
            w=Math.min(550,width-24);h=Math.min(308,height-24);
            left=(width-w)/2;top=(height-h)/2;
            String[] pages={"Overview","Supply","Delivery","Safety"};
            int side=132;
            for(int i=0;i<pages.length;i++) {
                final int p=i;
                addBtn(pages[i],left+12,top+65+30*i,side-24,23,()->{
                    tab=p;this.minecraft.setScreen(new UI(previous));
                });
            }
            addBtn("Close",left+12,top+h-36,side-24,22,()->onClose());
            int x=left+side+18, wid=w-side-31,half=(wid-10)/2;
            switch (page) {
                case 0 -> {
                    addBtn("Arm instance",x,top+131,half,23,()->{bot.arm(minecraft);refresh();});
                    addBtn(bot.running?"Stop":"Start",x+half+10,top+131,half,23,
                            ()->{if(bot.running)bot.stop("GUI");else bot.start(minecraft);refresh();});
                    addBtn("Lock target",x,top+165,half,23,()->{bot.lock(minecraft);refresh();});
                    addBtn("Mark pickup",x+half+10,top+165,half,23,()->{bot.picked();refresh();});
                    addBtn("Mark delivered",x,top+199,wid,23,()->{bot.delivered();refresh();});
                }
                case 1 -> {
                    addBtn("Automatic cast/reel  "+yes(cfg.fish),x,top+102,wid,23,()->{cfg.fish=!cfg.fish;save();});
                    addBtn("Rod slot  "+cfg.rod,x,top+140,half,23,()->{cfg.rod=cfg.rod%9+1;save();});
                    addBtn("Pull limit  "+cfg.maxPulls,x+half+10,top+140,half,23,
                            ()->{cfg.maxPulls=cfg.maxPulls%14+1;save();});
                    addBtn("Reel wait  "+cfg.reelDelay+"ms",x,top+178,wid,23,
                            ()->{cfg.reelDelay=cfg.reelDelay>=3000?900:cfg.reelDelay+150;save();});
                    addBtn("Smooth aim  "+yes(cfg.smooth),x,top+216,wid,23,()->{cfg.smooth=!cfg.smooth;save();});
                }
                case 2 -> {
                    addBtn("Set delivery center here",x,top+103,wid,23,()->{bot.center(minecraft);refresh();});
                    addBtn("Auto pearl  "+yes(cfg.pearl),x,top+145,wid,23,()->{cfg.pearl=!cfg.pearl;save();});
                    addBtn("Pearl slot  "+cfg.pearlSlot,x,top+187,wid,23,
                            ()->{cfg.pearlSlot=cfg.pearlSlot%9+1;save();});
                }
                case 3 -> {
                    addBtn("HUD  "+yes(cfg.overlay),x,top+102,wid,23,()->{cfg.overlay=!cfg.overlay;save();});
                    addBtn("Health threshold  "+cfg.minHealth,x,top+140,wid,23,
                            ()->{cfg.minHealth=cfg.minHealth>=20?2:cfg.minHealth+2;save();});
                    addBtn("EMERGENCY STOP",x,top+182,wid,23,()->{bot.stop("GUI safety stop");refresh();});
                }
            }
        }
        static String yes(boolean b){return b?"ON":"OFF";}
        void save(){cfg.save();refresh();}
        void refresh(){minecraft.setScreen(new UI(previous));}
        void addBtn(String label,int x,int y,int bw,int bh,Runnable action){
            addRenderableWidget(Button.builder(Component.literal(label),button->action.run()).bounds(x,y,bw,bh).build());
        }
        @Override public void onClose(){minecraft.setScreen(previous);}
        @Override public void extractRenderState(GuiGraphicsExtractor d,int mouseX,int mouseY,float delta){
            int side=132,x=left+side+18;
            d.fill(left,top,left+w,top+h,0xFF0D1118);
            d.fill(left,top,left+side,top+h,0xFF171D27);
            d.fill(left,top,left+w,top+3,0xFF63C9B5);
            d.fill(left+side,top+3,left+side+1,top+h,0xFF344055);
            d.fill(x,top+66,left+w-14,top+67,0xFF344055);
            super.extractRenderState(d,mouseX,mouseY,delta);
            print(d,"KUUDRA PILOT",left+12,top+17,0xFFEDF2F7);
            print(d,"FABRIC 26.1.2  v0.4",left+12,top+35,0xFF93A0B3);
            print(d,new String[]{"OVERVIEW","SUPPLY","DELIVERY","SAFETY"}[page],x,top+18,0xFFE9EDF2);
            print(d,"Settings & diagnostics",x,top+38,0xFF7BCDBD);
            if(page==0){
                d.fill(x,top+80,left+w-16,top+123,0xFF19212C);
                print(d,"STATE  "+bot.state,x+10,top+86,bot.running?0xFF7BCDBD:0xFFEDF2F7);
                print(d,"Crates "+bot.delivered+"    Pulls "+bot.pulls+"    Failures "+bot.failures,
                        x+10,top+104,0xFF9BACC2);
                print(d,"Use /kp center when standing on middle platform",x,top+238,0xFF9BACC2);
            } else if(page==1){
                print(d,"Experimental supply cast/reel",x,top+82,0xFFA7B6C9);
                print(d,"Targets named crates or /kp lock",x,top+251,0xFFA7B6C9);
            } else if(page==2){
                print(d,cfg.centerSaved ?
                        String.format(Locale.US,"Center: %.0f / %.0f / %.0f",cfg.centerX,cfg.centerY,cfg.centerZ)
                        : "Center: NOT SET",x,top+80,0xFF7BCDBD);
                print(d,"Pearl path requires live calibration",x,top+251,0xFFA7B6C9);
            } else {
                print(d,"Stop on damage, lava, world change, retry limit",x,top+81,0xFFA7B6C9);
                print(d,"RSHIFT menu    F9 toggle    F8 stop",x,top+250,0xFFA7B6C9);
            }
            print(d,shorten(bot.detail,48),x,top+h-29,0xFF83D4C3);
        }
        void print(GuiGraphicsExtractor d,String t,int x,int y,int color){
            d.text(this.font,t,x,y,color,false);
        }
    }
}
