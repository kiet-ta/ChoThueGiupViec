# Feature Overiew
In file demo, we have:
Folder Users - contains all user related files
In folder Users:
- Folder Command like a DTO: CreateUserCommand, UpdateUserCommand,...
- Folder Handler: CreateUserHandler, UpdateUserHandler,...

## What is Command?
Is a symbolic representation of an action to be performed, accompanied by associated data.
Command = what you want to do

## What is Handler?
Is where that action is handled — receiving the Command, performing the logic, and returning the result.
Handler = who makes it happen

# Real-life example: Online shopping
Command = “PlaceOrderCommand” -> includes product ID, quantity, shipping address.

Handler = processes the order -> checks stock, creates order, saves to database, returns order ID.
