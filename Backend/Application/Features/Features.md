# Feature Overview
In modular CQRS features, we have:
Folder Customers - contains customer related feature files
In folder Customers:
- Folder Commands: CreateCustomerCommand, UpdateCustomerCommand,...
- Folder Handlers: CreateCustomerHandler, UpdateCustomerHandler,...

## What is Command?
Is a symbolic representation of an action to be performed, accompanied by associated data.
Command = what you want to do

## What is Handler?
Is where that action is handled — receiving the Command, performing the logic, and returning the result.
Handler = who makes it happen

# Real-life example: Booking service
Command = CreateJobOrderCommand -> includes service tier, address ID, required workers.

Handler = processes the order -> validates rules, creates job order, saves to database, returns order ID.
