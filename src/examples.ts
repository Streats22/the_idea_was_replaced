export const EXAMPLES: Record<string, string> = {
  "Starter: harvest grass": `# You are the drone now. Write code, then press Run (Ctrl+Enter).
# The farm wraps around: moving off one edge brings you back on the other.

while True:
    for i in range(3):
        for j in range(3):
            if can_harvest():
                harvest()
            move(North)
        move(East)
`,
  "Bushes for wood": `# Needs: Planting, Senses
def tend():
    if can_harvest():
        harvest()
        plant(Entities.Bush)
    elif get_entity_type() == Entities.Grass:
        plant(Entities.Bush)

while True:
    for x in range(get_world_size()):
        for y in range(get_world_size()):
            tend()
            move(North)
        move(East)
`,
  "Mixed farm": `# Needs: Carrots, Trees, Senses
# Column 0 grows hay, odd tiles grow trees, the rest carrots.
def tend(x, y):
    kind = get_entity_type()
    if kind != None and not can_harvest():
        return
    harvest()
    if x == 0:
        if get_ground_type() == Grounds.Soil:
            till()
    elif (x + y) % 2 == 1:
        plant(Entities.Tree)
    else:
        if get_ground_type() == Grounds.Grassland:
            till()
        plant(Entities.Carrot)

while True:
    for x in range(get_world_size()):
        for y in range(get_world_size()):
            tend(x, y)
            move(North)
        move(East)
`,
  "Pumpkin patch": `# Needs: Pumpkins
# Fill the whole farm with pumpkins, replant dead ones,
# then harvest everything as one giant group.
n = get_world_size()

def sweep():
    ready = True
    for x in range(n):
        for y in range(n):
            if get_ground_type() == Grounds.Grassland:
                till()
            kind = get_entity_type()
            if kind == Entities.Dead_Pumpkin or kind == None:
                harvest()
                plant(Entities.Pumpkin)
                ready = False
            elif not can_harvest():
                ready = False
            move(North)
        move(East)
    return ready

while True:
    if sweep():
        harvest()
        print("Pumpkins:", num_items(Items.Pumpkin))
`,
};

export const DEFAULT_CODE = EXAMPLES["Starter: harvest grass"];
