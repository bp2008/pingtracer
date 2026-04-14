<template>
	<header>
		<img alt="Vue logo" class="logo" src="@/assets/logo.svg" width="125" height="125" />

		<div v-for="response in RpcResponses">{{ response }}</div>

	</header>

	<RouterView />
</template>

<script>
	import { RouterLink, RouterView } from 'vue-router'
	import ExecAPI from './library/API';
	import RPC from './library/RPC';

	export default {
		components: { RouterLink, RouterView },
		data()
		{
			return {
				RpcResponses: [],
			};
		},
		async created()
		{
			this.RpcResponses.push(await RPC.ExampleController.ExampleRpcMethodWithNoArgs());
			this.RpcResponses.push(await RPC.ExampleController.ExampleRpcMethodWithTwoArgs("RPC Call arg1", 2));
			this.RpcResponses.push("The time is '" + await ExecAPI("ExampleController/ExampleApiMethodWithNoArgument") + "'.");
			let complexResult = await ExecAPI("ExampleController/ExampleApiMethodWithNumbersArgument", { numbers: [100, 3, 20] });
			this.RpcResponses.push(complexResult.message);
			try
			{
				this.RpcResponses.push(await RPC.ExampleController.ExampleMethodReturnsError());
				this.RpcResponses.push("assertion failed. expected error from RPC.ExampleController.ExampleMethodReturnsError()!");
			}
			catch (ex)
			{
				this.RpcResponses.push(ex.message);
			}
			try
			{
				this.RpcResponses.push(await ExecAPI("ExampleController/ExampleMethodReturnsError"));
				this.RpcResponses.push('assertion failed. expected error from ExecAPI("ExampleController/ExampleMethodReturnsError")!');
			}
			catch (ex)
			{
				this.RpcResponses.push(ex.message);
			}
		}
	}
</script>

<style scoped>
</style>
