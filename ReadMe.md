

| Issue(No exceptions)                                   | General plan                                                 | Type        |
| ------------------------------------------------------ | ------------------------------------------------------------ | ----------- |
| *Horizontal projections                                | compare the connection area and the objects' projection      | Class2      |
| *Stairs ???                                            | Pass                                                         | Class1 or 2 |
| <u>Ceiling objects height > 2032mm</u>                 | <u>A ray from the object down to hit the floor and cal the len of ray segment.</u> | Class2      |
| *exit route width. If more than one exit, > 50% each   | easy. occupant(can be refered from a form) * 5.1mm(minimum width see the form) | Class2      |
| <u>exit number</u>                                     | <u>Count the door which function is "Exterior"</u>           | Class1      |
| *exit location                                         | To exit, cal the dis of 2 points. To doorways, cal the dis of 2 lines. |             |
| *Illumination (>1lux,<11lux, including aisle corridor) | cal the light on the object(like exit route groud)           |             |
| <u>Exit door regulations</u>                           | <u>Size and max degree: easy.</u>                            | Class 1     |
| !Travel distance and path                              | Use revit 2020 api(pathoftraavel)                                                                         Find all doors which connect to the outside, can be used as exits.            Find the nearest exit to a room and then calculate the travel distance from rooms to nearest exits.<br />We can select some rooms manually and all travel paths cannot pass through them.<br />We can select some categories and all elements belong to them will be ignored(They won’t be obstacles any more). | Class2.     |



