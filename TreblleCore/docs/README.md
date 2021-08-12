# Treblle for .NET Core

  
  

Treblle makes it super easy to understand what’s going on with your APIs and the apps that use them. Just by adding Treblle to your API out of the box you get:

* Real-time API monitoring and logging

* Auto-generated API docs with OAS support

* API analytics

* Quality scoring

* One-click testing

* API managment on the go

* and more...

  

## Requirements

* .NET Core 3.0

  

## Getting started

Create a FREE account on <https://treblle.com> to get an API key and Project ID.

  

You can install Treblle .NET Core via NuGet Package Manager or by running the following command:

```bash

dotnet add package Treblle.Net.Core

```

  

You will be prompted to enter your Treblle API key and Project ID. Your settings will be saved in ```app.config``` and you can always edit them there.

Here is an example:

  

```xml

<configuration>
	<appSettings>
		<add  key="TreblleApiKey"  value="{Your_API_Key}"  />
		<add  key="TreblleProjectId"  value="{Your_Project_Id}"  />
	</appSettings>
</configuration>

```

  

Next you'll need to add this to your ``` Configure(IApplicationBuilder app, IWebHostEnvironment env) ``` method in ```Startup.cs```:

  
  

```csharp

app.Use(next => new  RequestDelegate(
	async  context =>
	{
		context.Request.EnableBuffering();
		await  next(context);
	}
));

```

Now you can specify which endpoints you want Treblle to track by adding this simple attribute to any API controller or method:

  

```csharp

[Treblle]

```

  

That's it. Your API requests and responses are now being sent to your Treblle project. Just by adding a few lines of code you get features like: auto-documentation, real-time request/response monitoring, error tracking and so much more.

  
  

### Need to hide additional fields?

If you want to expand the list of fields you want to hide, you can pass property names you want to hide by adding the ```AdditionalFieldsToMask``` property to your ```app.config``` file like in the example below.

  

```xml

<configuration>
	<appSettings>
		<add  key="TreblleApiKey"  value="{Your_API_Key}"  />
		<add  key="TreblleProjectId"  value="{Your_Project_Id}"  />
		<add  key="AdditionalFieldsToMask"  value="secretField,highlySensitiveField"  />
	</appSettings>
</configuration>

```

  

## Support

If you have problems of any kind feel free to reach out via <https://treblle.com> or email vedran@treblle.com and we'll do our best to help you out.

  

## License

Copyright 2021, Treblle Limited. Licensed under the MIT license:

http://www.opensource.org/licenses/mit-license.php