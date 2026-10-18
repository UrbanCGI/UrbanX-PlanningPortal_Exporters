	local bStepped = for i = 1 to bLayerOld.count collect false

	local bLayers = for i = 1 to bLayerOld.count collect (if bLayerOld[i] == "" then undefined else (LayerManager.getLayerFromName bLayerOld[i]))
	for i = 1 to bLayers.count where bLayerOld[i] != "" and bLayers[i] == undefined do (format "FAIL\tlayerfind\t%\tgone\n" i to:bLog)
	local bNodes = #()
	for i = 1 to bNodeHandle.count do
	(
		bNode = undefined
		if bNodeHandle[i] != 0 do
		(
			bNode = maxOps.getNodeByHandle bNodeHandle[i]
			if (bNode == undefined) then
			(
				format "FAIL\tfind\t%\tgone\n" i to:bLog
			)
			else if (bNode.name != bNodeOld[i]) then
			(
				format "FAIL\tfind\t%\tchanged\n" i to:bLog
				bNode = undefined
			)
		)
		append bNodes bNode
	)
	try
	(
		undo "Add layer" on
		(
			with redraw off
			(

				for k = 1 to bLayerRenameIdx.count do
				(
					local i = bLayerRenameIdx[k]
					try
					(
						bLayer = bLayers[i]
						if (bLayer != undefined) do
						(
							bLayer.setName bLayerRenameTemp[k]
							if (bLayer.name == bLayerRenameTemp[k]) then (bStepped[i] = true) else (format "FAIL\tlayer\t%\trefused\n" i to:bLog)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tlayer\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for k = 1 to bLayerRenameIdx.count do
				(
					local i = bLayerRenameIdx[k]
					if bStepped[i] do
					(
						try
						(
							bLayer = bLayers[i]
							bLayer.setName bLayerNew[i]
							if (bLayer.name == bLayerNew[i]) then
							(
								bLayersRenamed += 1
							)
							else
							(
								bLayer.setName bLayerOld[i]
								if (bLayer.name == bLayerOld[i]) then (format "FAIL\tlayer\t%\ttaken\n" i to:bLog) else (format "FAIL\tlayer\t%\tstuck\n" i to:bLog)
							)
						)
						catch
						(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tlayer\t%\terror\t%\n" i bDetail to:bLog
						)
					)
				)

				for k = 1 to bLayerCreateIdx.count do
				(
					local i = bLayerCreateIdx[k]
					try
					(
						bLayer = LayerManager.newLayerFromName bLayerNew[i]
						if (bLayer == undefined) then
						(
							format "FAIL\tlayercreate\t%\ttaken\n" i to:bLog
						)
						else
						(
							bLayers[i] = bLayer
							bLayersCreated += 1
							if (bLayerParent[i] > 0) do
							(
								bParent = bLayers[bLayerParent[i]]
								if (bParent == undefined) then (format "FAIL\tlayerparent\t%\tgone\n" i to:bLog) else (bLayer.setParent bParent)
							)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tlayercreate\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for k = 1 to bLayerMoveIdx.count do
				(
					local i = bLayerMoveIdx[k]
					try
					(
						bLayer = bLayers[i]
						bParent = bLayers[bLayerParent[i]]
						if (bLayer == undefined or bParent == undefined) then
						(
							format "FAIL\tlayermove\t%\tgone\n" i to:bLog
						)
						else
						(
							bLayer.setParent bParent
							bOther = bLayer.getParent()
							if (bOther != undefined and bOther.name == bParent.name) then (bLayersMoved += 1) else (format "FAIL\tlayermove\t%\trefused\n" i to:bLog)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tlayermove\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for k = 1 to bNodeRenameIdx.count do
				(
					local i = bNodeRenameIdx[k]
					try
					(
						bNode = bNodes[i]
						if (bNode != undefined) do
						(
							bNode.name = bNodeNew[i]
							if (bNode.name == bNodeNew[i]) then (bHelpersRenamed += 1) else (format "FAIL\thelper\t%\trefused\n" i to:bLog)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\thelper\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for k = 1 to bNodeCreateIdx.count do
				(
					local i = bNodeCreateIdx[k]
					try
					(
						bLayer = if (bNodeLayer[i] > 0) then bLayers[bNodeLayer[i]] else undefined
						if (bLayer == undefined) then
						(
							format "FAIL\thelpercreate\t%\tnolayer\n" i to:bLog
						)
						else
						(
							bNode = Dummy name:bNodeNew[i] pos:[0,0,0] boxsize:[1,1,1]
							bLayer.addNode bNode
							bNodes[i] = bNode
							bHelpersCreated += 1
							if (bNodeId[i] != "") do
							(
								setUserProp bNode "planner_id" bNodeId[i]
								bIdsStamped += 1
							)
							if (bNodeParent[i] > 0) do
							(
								bParent = bNodes[bNodeParent[i]]
								if (bParent == undefined) then
								(
									format "FAIL\thelperparent\t%\tgone\n" i to:bLog
								)
								else
								(
									if (isGroupHead bParent) then
									(
										if (isGroupMember bNode) do (detachNodesFromGroup #(bNode))
										attachNodesToGroup #(bNode) bParent
									)
									else
									(
										if ((isGroupMember bNode) and not (isGroupMember bParent)) do (detachNodesFromGroup #(bNode))
										bNode.parent = bParent
									)
									if (bNode.parent != bParent) do (format "FAIL\thelperparent\t%\trefused\n" i to:bLog)
								)
							)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\thelpercreate\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for k = 1 to bNodePutIdx.count do
				(
					local i = bNodePutIdx[k]
					try
					(
						bNode = bNodes[i]
						bLayer = bLayers[bNodeLayer[i]]
						if (bNode != undefined) do
						(
							if (bLayer == undefined) then
							(
								format "FAIL\tput\t%\tnolayer\n" i to:bLog
							)
							else
							(
								bLayer.addNode bNode
								if (bNode.layer != undefined and bNode.layer.name == bLayer.name) then (bHelpersPut += 1) else (format "FAIL\tput\t%\trefused\n" i to:bLog)
							)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tput\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for k = 1 to bNodeMoveIdx.count do
				(
					local i = bNodeMoveIdx[k]
					try
					(
						bNode = bNodes[i]
						bParent = bNodes[bNodeParent[i]]
						if (bNode != undefined) do
						(
							if (bParent == undefined) then
							(
								format "FAIL\thelpermove\t%\tgone\n" i to:bLog
							)
							else
							(
								if (isGroupHead bParent) then
								(
									if (isGroupMember bNode) do (detachNodesFromGroup #(bNode))
									attachNodesToGroup #(bNode) bParent
								)
								else
								(
									if ((isGroupMember bNode) and not (isGroupMember bParent)) do (detachNodesFromGroup #(bNode))
									bNode.parent = bParent
								)
								if (bNode.parent == bParent) then (bHelpersMoved += 1) else (format "FAIL\thelpermove\t%\trefused\n" i to:bLog)
							)
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\thelpermove\t%\terror\t%\n" i bDetail to:bLog
					)
				)

				for k = 1 to bNodeStampIdx.count do
				(
					local i = bNodeStampIdx[k]
					try
					(
						bNode = bNodes[i]
						if (bNode != undefined) do
						(
							setUserProp bNode "planner_id" bNodeId[i]
							bIdsStamped += 1
						)
					)
					catch
					(
						bDetail = (getCurrentException()) as string
						bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
						format "FAIL\tstamp\t%\terror\t%\n" i bDetail to:bLog
					)
				)
			)
		)
	)
	catch
	(
		bDetail = (getCurrentException()) as string
		bDetail = substituteString (substituteString (substituteString bDetail "\r" " ") "\n" " ") "\t" " "
		format "ABORT\t%\n" bDetail to:bLog
	)
	format "DONE\t%\t%\t%\t%\t%\t%\t%\t%\n" bLayersRenamed bLayersCreated bLayersMoved bHelpersRenamed bHelpersCreated bHelpersMoved bHelpersPut bIdsStamped to:bLog
	format "PLANNERBUILD-END\n" to:bLog
	bLog as string
)
