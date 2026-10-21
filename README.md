# Smoothie-Engine
## Source code may be outdated from the .exe!
------------------
## Features
- Physics Simulation
- Scripting
- Art Creation
- Game Development

## How to download Smoothie

Simply go to the releases tab or visit
https://github.com/seventyyyyyyfour-cell/Smoothie-Engine/releases/tag/MainUpdate
and download the EXE file.

## How to use Smoothie

### Importing .smoothieproject files
- Go to the folder where the .exe is store
- Create a folder inside named "Projects" if its not alread there
- Place the .smoothieproject files in the "Projects" folder.

### Scripting
- To create a script select any object and add it as a "Modifier"
- Name the script then go to the visual scripting tab at the top.
- Select the object you set the modifer on then start scripting
- For what each node does scroll below


## Node Definitions

### Start
 - Fires ones on the games start
### Update
 - Fires every up frame
### Key
 - Gets key input and can be toggled between Held, Pushed, Released
### Condition
 - Acts as an If and outputs a bool
### And
 - If all inputs are true then the output will fire
### Move
 - Moves an object by an amount per second or in an instant
### Rotate
 - Rotates an object by an amount per second or in an instant
### Scale
 - Scales an object by an amount per second or in an instant
### New Object
 - Unfuctional
### Var
 - Sets or changes a variable
### Math
 - Performs a math operation between two floats
### Vec2
 - Puts together a Vector2 from a floatc
### Get Pos
 - Gets the position of an object and sets it to a Vector2
### Repeat Loop
 - Repeats the code until a condition is met
