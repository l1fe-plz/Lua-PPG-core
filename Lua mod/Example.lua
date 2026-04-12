print("Mod is work!!! :D")
function HatCreate(tr, sprite)
    local hat = New(GameObject, "Cool Hat")
    local render = AddComponent(hat, SpriteRenderer)
    render.sprite = sprite
    SetParent(hat.transform, tr, false)
    local rederer2 = tr.GetComponent("SpriteRenderer")
    render.sortingLayerName = rederer2.sortingLayerName
    render.sortingOrder = rederer2.sortingOrder + 1
end

function Main()
    ModAPI.RegisterCategory("Lua", "Mod with Lua", NewSprite("c.png"));

    local brickMod = Modification.__new()

    brickMod.OriginalItem = ModAPI.FindSpawnable("Brick")
    brickMod.NameOverride = "FUCKING BIG LUA"
    brickMod.DescriptionOverride = "just mod icon :( \n wait... where is it?"
    brickMod.CategoryOverride = ModAPI.FindCategory("Lua")
    brickMod.ThumbnailOverride = NewSprite("c.png")

    brickMod.AfterSpawn = Action(function(instance)
        local renderer = instance.GetComponent("SpriteRenderer")
        renderer.sprite = NewSprite("c.png")
        Utils.FixColliders(instance)
        function cl()
            while renderer.color.a > 0.1 do
                renderer.color = New(Color, renderer.color.r, renderer.color.g, renderer.color.b, renderer.color.a - 0.01)
                coroutine.yield(0.1)
                print(renderer.color)
            end
        end
        startCoroutine(cl)
    end)

    local HumanMod = Modification.__new()

    HumanMod.OriginalItem = ModAPI.FindSpawnable("Human")
    HumanMod.NameOverride = "Lua Human!! With cool hat!!!"
    HumanMod.DescriptionOverride = "He likes lua xd"
    HumanMod.CategoryOverride = ModAPI.FindCategory("Lua")
    HumanMod.ThumbnailOverride = NewSprite("icon.png")

    HumanMod.AfterSpawn = Action(function(instance)
        local person = instance.GetComponent("PersonBehaviour")
        --person.SetBodyTextures(NewTexture("modicon.png"))
        local limbs = person.Limbs
        HatCreate(limbs[1].transform, NewSprite("hat.png")) --Теперь Array это Table, баг за багом год за годом
        for i = 1, #limbs do
            limbs[i].GetComponent("SpriteRenderer").color = New(Color, 0.5, 0.5, 1, 1)
        end
        local events = AddLuaEvents(limbs[1].gameObject)
        events:AddCollisionEnter(function(coll)
            ModAPI.Notify("Stupid ".. tostring(coll.gameObject.name) .." touched me!")
        end)
    end)
    ModAPI.Register(brickMod)
    ModAPI.Register(HumanMod)
    print("cool :) (you cant see this in logs)")
end

function OnUnload()
    print("Nooo! Dont unload me! :(")
end